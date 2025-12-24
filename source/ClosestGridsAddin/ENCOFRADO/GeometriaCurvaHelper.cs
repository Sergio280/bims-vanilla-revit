using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.ENCOFRADO
{
    /// <summary>
    /// Maneja la creación de encofrados para geometría curva (columnas circulares, etc.)
    /// utilizando masas conceptuales para cortar los elementos
    /// </summary>
    public static class GeometriaCurvaHelper
    {
        private const double MIN_VOLUMEN = 0.0001; // Volumen mínimo para considerar intersección

        /// <summary>
        /// Crea encofrado para cara cilíndrica usando masa conceptual para cortar
        /// </summary>
        public static Wall CrearEncofradoColumnaCircular(
            Document doc,
            CylindricalFace caraCilindrica,
            WallType wallType,
            Level nivel,
            double altura,
            List<Element> elementosAdyacentes)
        {
            try
            {
                // PASO 1: Extraer arco de la cara cilíndrica
                Curve curvaBase = ExtraerCurvaDeCaraCilindrica(caraCilindrica, nivel.Elevation);

                if (curvaBase == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ No se pudo extraer curva de cara cilíndrica");
                    return null;
                }

                // PASO 2: Crear muro curvo base
                Wall muroCurvo = Wall.Create(
                    doc,
                    curvaBase,
                    wallType.Id,
                    nivel.Id,
                    altura,
                    0,      // offset
                    false,  // flip
                    false); // structural

                if (muroCurvo == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ No se pudo crear muro curvo");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Muro curvo creado: ID={muroCurvo.Id}");

                // PASO 3: Aplicar cortes con masas conceptuales para elementos adyacentes
                int cortesAplicados = AplicarCortesConMasas(doc, muroCurvo, elementosAdyacentes);

                System.Diagnostics.Debug.WriteLine($"✅ {cortesAplicados} cortes aplicados al muro curvo");

                return muroCurvo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error en CrearEncofradoColumnaCircular: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Aplica cortes al muro usando masas conceptuales
        /// </summary>
        private static int AplicarCortesConMasas(
            Document doc,
            Wall muroCurvo,
            List<Element> elementosAdyacentes)
        {
            int cortesAplicados = 0;

            // Obtener sólido del muro para verificar intersecciones
            Solid solidoMuro = EncofradoBaseHelper.ObtenerSolidoPrincipal(muroCurvo);
            if (solidoMuro == null) return 0;

            foreach (var elemAdyacente in elementosAdyacentes)
            {
                try
                {
                    Solid solidoAdyacente = EncofradoBaseHelper.ObtenerSolidoPrincipal(elemAdyacente);
                    if (solidoAdyacente == null) continue;

                    // Verificar si hay intersección real
                    Solid interseccion = null;
                    try
                    {
                        interseccion = BooleanOperationsUtils.ExecuteBooleanOperation(
                            solidoMuro,
                            solidoAdyacente,
                            BooleanOperationsType.Intersect);
                    }
                    catch { }

                    if (interseccion != null && interseccion.Volume > MIN_VOLUMEN)
                    {
                        // HAY intersección, crear masa de corte
                        DirectShape masaCorte = CrearMasaDeCorte(doc, solidoAdyacente, elemAdyacente.Id);

                        if (masaCorte != null)
                        {
                            // Aplicar corte al muro
                            bool corteExitoso = AplicarCorteConMasa(doc, muroCurvo, masaCorte);

                            if (corteExitoso)
                            {
                                cortesAplicados++;
                                System.Diagnostics.Debug.WriteLine(
                                    $"  ✅ Corte aplicado desde elemento {elemAdyacente.Id}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"  ⚠️ No se pudo aplicar corte desde elemento {elemAdyacente.Id}: {ex.Message}");
                }
            }

            return cortesAplicados;
        }

        /// <summary>
        /// Crea una masa conceptual (DirectShape categoría Mass) para cortar
        /// </summary>
        private static DirectShape CrearMasaDeCorte(Document doc, Solid solidoCorte, ElementId idElementoOriginal)
        {
            try
            {
                // IMPORTANTE: Debe ser categoría OST_Mass para que InstanceVoidCutUtils funcione
                DirectShape masaCorte = DirectShape.CreateElement(
                    doc,
                    new ElementId(BuiltInCategory.OST_Mass));

                masaCorte.SetShape(new GeometryObject[] { solidoCorte });
                masaCorte.Name = $"MasaCorte_{idElementoOriginal}";

                return masaCorte;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error creando masa de corte: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Aplica el corte de una masa a un muro usando InstanceVoidCutUtils
        /// </summary>
        private static bool AplicarCorteConMasa(Document doc, Wall muro, DirectShape masaCorte)
        {
            try
            {
                // Verificar que no exista ya el corte
                if (InstanceVoidCutUtils.CanBeCutWithVoid(muro))
                {
                    InstanceVoidCutUtils.AddInstanceVoidCut(doc, muro, masaCorte);
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("  ⚠️ El muro no puede ser cortado con void");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"  ❌ Error aplicando corte: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extrae una curva (Arc o Circle) de una cara cilíndrica
        /// </summary>
        private static Curve ExtraerCurvaDeCaraCilindrica(
            CylindricalFace caraCilindrica,
            double elevacion)
        {
            try
            {
                // Obtener los parámetros del cilindro
                var cylindricalSurface = caraCilindrica.GetSurface() as CylindricalSurface;
                if (cylindricalSurface == null) return null;

                XYZ origen = cylindricalSurface.Origin;
                XYZ eje = cylindricalSurface.Axis;
                double radio = cylindricalSurface.Radius;

                // Proyectar el origen al nivel especificado
                XYZ origenProyectado = ProyectarPuntoAElevacion(origen, eje, elevacion);

                // Crear plano perpendicular al eje en la elevación
                Plane planoCorte = Plane.CreateByNormalAndOrigin(eje, origenProyectado);

                // Método 1: Intentar crear círculo completo
                try
                {
                    // Crear curva circular completa (360 grados)
                    Arc circuloCompleto = Arc.Create(
                        origenProyectado,
                        radio,
                        0,
                        2 * Math.PI,
                        planoCorte.XVec,
                        planoCorte.YVec);

                    return circuloCompleto;
                }
                catch { }

                // Método 2: Crear arco desde edges de la cara
                var edgeLoops = caraCilindrica.EdgeLoops;
                if (edgeLoops.Size > 0)
                {
                    EdgeArray edgeArray = edgeLoops.get_Item(0);
                    foreach (Edge edge in edgeArray)
                    {
                        Curve edgeCurve = edge.AsCurve();

                        // Si es un arco, proyectarlo al nivel
                        if (edgeCurve is Arc arc)
                        {
                            // Proyectar puntos del arco
                            XYZ p0 = new XYZ(arc.GetEndPoint(0).X, arc.GetEndPoint(0).Y, elevacion);
                            XYZ p1 = new XYZ(arc.Evaluate(0.5, true).X, arc.Evaluate(0.5, true).Y, elevacion);
                            XYZ p2 = new XYZ(arc.GetEndPoint(1).X, arc.GetEndPoint(1).Y, elevacion);

                            return Arc.Create(p0, p2, p1);
                        }
                    }
                }

                // Método 3: Fallback - crear arco manualmente
                XYZ punto1 = origenProyectado + radio * planoCorte.XVec;
                XYZ puntoMedio = origenProyectado + radio * planoCorte.YVec;
                XYZ punto2 = origenProyectado - radio * planoCorte.XVec;

                return Arc.Create(punto1, punto2, puntoMedio);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error extrayendo curva cilíndrica: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Proyecta un punto a una elevación específica a lo largo de un eje
        /// </summary>
        private static XYZ ProyectarPuntoAElevacion(XYZ punto, XYZ eje, double elevacion)
        {
            // Si el eje es vertical (Z), simplemente ajustar Z
            if (Math.Abs(eje.Z) > 0.99)
            {
                return new XYZ(punto.X, punto.Y, elevacion);
            }

            // Para ejes inclinados, calcular proyección
            double t = (elevacion - punto.Z) / eje.Z;
            return punto + t * eje;
        }

        /// <summary>
        /// Detecta si una cara es cilíndrica
        /// </summary>
        public static bool EsCaraCilindrica(Face face)
        {
            return face is CylindricalFace;
        }

        /// <summary>
        /// Obtiene todas las caras cilíndricas de un sólido
        /// </summary>
        public static List<CylindricalFace> ObtenerCarasCilindricas(Solid solido)
        {
            List<CylindricalFace> carasCilindricas = new List<CylindricalFace>();

            try
            {
                foreach (Face face in solido.Faces)
                {
                    if (face is CylindricalFace cylindricalFace)
                    {
                        carasCilindricas.Add(cylindricalFace);
                    }
                }
            }
            catch { }

            return carasCilindricas;
        }

        /// <summary>
        /// Crea encofrado para múltiples caras cilíndricas (ej: columna circular completa)
        /// </summary>
        public static List<Wall> CrearEncofradoCompletoCilindrico(
            Document doc,
            Solid solidoColumna,
            WallType wallType,
            Level nivel,
            double altura,
            List<Element> elementosAdyacentes)
        {
            List<Wall> murosCreados = new List<Wall>();

            try
            {
                var carasCilindricas = ObtenerCarasCilindricas(solidoColumna);

                System.Diagnostics.Debug.WriteLine($"🔍 Caras cilíndricas encontradas: {carasCilindricas.Count}");

                foreach (var cara in carasCilindricas)
                {
                    Wall muro = CrearEncofradoColumnaCircular(
                        doc, cara, wallType, nivel, altura, elementosAdyacentes);

                    if (muro != null)
                    {
                        murosCreados.Add(muro);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error en CrearEncofradoCompletoCilindrico: {ex.Message}");
            }

            return murosCreados;
        }
    }
}
