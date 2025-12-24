using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.ENCOFRADO
{
    /// <summary>
    /// Sistema integrado de encofrado que combina:
    /// - Reglas por tipo de elemento (ReglasEncofrado)
    /// - Dirección de extrusión correcta (DireccionExtrusionHelper)
    /// - Recortes automáticos con operaciones booleanas
    /// - Creación de Wall/Floor nativos con curvas recortadas
    /// </summary>
    public static class EncofradoIntegradoHelper
    {
        private const double TOLERANCIA_VOLUMEN = 0.0001;

        /// <summary>
        /// Crea encofrado completo para un elemento estructural
        /// Retorna lista de elementos nativos (Wall/Floor) creados
        /// </summary>
        public static List<Element> CrearEncofradoCompleto(
            Document doc,
            Element elementoEstructural,
            WallType wallType,
            FloorType floorType,
            List<Element> elementosAdyacentes = null)
        {
            var elementosCreados = new List<Element>();

            try
            {
                // Obtener sólido del elemento
                Solid solido = EncofradoBaseHelper.ObtenerSolidoPrincipal(elementoEstructural);
                if (solido == null) return elementosCreados;

                // Obtener elementos adyacentes si no se proporcionaron
                if (elementosAdyacentes == null)
                {
                    elementosAdyacentes = EncofradoBaseHelper.ObtenerElementosAdyacentes(
                        doc, elementoEstructural);
                }

                // Procesar cada cara del elemento
                foreach (Face face in solido.Faces)
                {
                    Element encofradoCreado = null;

                    // Caso 1: Cara planar
                    if (face is PlanarFace planarFace)
                    {
                        encofradoCreado = CrearEncofradoParaCaraPlanar(
                            doc,
                            elementoEstructural,
                            planarFace,
                            solido,
                            wallType,
                            floorType,
                            elementosAdyacentes);
                    }
                    // Caso 2: Cara cilíndrica (columnas circulares)
                    else if (face is CylindricalFace cylindricalFace)
                    {
                        encofradoCreado = CrearEncofradoParaCaraCilindrica(
                            doc,
                            cylindricalFace,
                            wallType,
                            elementosAdyacentes);
                    }

                    if (encofradoCreado != null)
                    {
                        elementosCreados.Add(encofradoCreado);
                    }
                }

                return elementosCreados;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"❌ Error en CrearEncofradoCompleto: {ex.Message}");
                return elementosCreados;
            }
        }

        /// <summary>
        /// Crea encofrado para una cara planar
        /// </summary>
        private static Element CrearEncofradoParaCaraPlanar(
            Document doc,
            Element elementoEstructural,
            PlanarFace cara,
            Solid solidoElemento,
            WallType wallType,
            FloorType floorType,
            List<Element> elementosAdyacentes)
        {
            try
            {
                // PASO 1: Verificar si debe encofrarse según reglas
                bool debeEncofrar = ReglasEncofrado.DebeEncofrarCara(
                    elementoEstructural,
                    cara,
                    out TipoElementoEncofrado tipoEncofrado);

                if (!debeEncofrar || tipoEncofrado == TipoElementoEncofrado.NoDefinido)
                {
                    return null;
                }

                // PASO 2: Calcular dirección de extrusión HACIA AFUERA
                XYZ direccionExtrusion = DireccionExtrusionHelper.ObtenerDireccionHaciaAfuera(
                    cara, solidoElemento);

                // PASO 3: Obtener espesor del tipo seleccionado
                double espesor = ObtenerEspesorDeTipo(
                    tipoEncofrado == TipoElementoEncofrado.Muro ? wallType : null,
                    tipoEncofrado == TipoElementoEncofrado.Suelo ? floorType : null);

                // PASO 4: Crear DirectShape con geometría recortada
                DirectShape dsRecortado = CrearDirectShapeConRecortes(
                    doc,
                    cara,
                    direccionExtrusion,
                    espesor,
                    elementosAdyacentes,
                    elementoEstructural);

                if (dsRecortado == null) return null;

                // PASO 5: Extraer curvas del DirectShape recortado
                List<Curve> curvasRecortadas = ExtraerCurvasDeDirectShape(dsRecortado, cara);

                if (curvasRecortadas == null || curvasRecortadas.Count < 3)
                {
                    // Si falla extracción, mantener DirectShape
                    return dsRecortado;
                }

                // PASO 6: Crear Wall o Floor nativo con curvas recortadas
                Element elementoNativo = null;

                if (tipoEncofrado == TipoElementoEncofrado.Muro)
                {
                    elementoNativo = CrearMuroNativoConCurvas(
                        doc, curvasRecortadas, wallType, dsRecortado);
                }
                else if (tipoEncofrado == TipoElementoEncofrado.Suelo)
                {
                    elementoNativo = CrearSueloNativoConCurvas(
                        doc, curvasRecortadas, floorType, dsRecortado);
                }

                // PASO 7: Si se creó nativo exitosamente, eliminar DirectShape temporal
                if (elementoNativo != null)
                {
                    doc.Delete(dsRecortado.Id);
                    return elementoNativo;
                }
                else
                {
                    // Si falló, mantener DirectShape
                    return dsRecortado;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"❌ Error en CrearEncofradoParaCaraPlanar: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Crea DirectShape con recortes aplicados por elementos adyacentes
        /// </summary>
        private static DirectShape CrearDirectShapeConRecortes(
            Document doc,
            PlanarFace cara,
            XYZ direccionExtrusion,
            double espesor,
            List<Element> elementosAdyacentes,
            Element elementoOriginal)
        {
            try
            {
                // Crear sólido de encofrado inicial
                var curveLoops = cara.GetEdgesAsCurveLoops();
                if (curveLoops == null || curveLoops.Count == 0) return null;

                Solid solidoEncofrado = GeometryCreationUtilities.CreateExtrusionGeometry(
                    curveLoops, direccionExtrusion, espesor);

                if (solidoEncofrado == null || solidoEncofrado.Volume < TOLERANCIA_VOLUMEN)
                    return null;

                // Aplicar recortes por elementos adyacentes
                int descuentos = 0;
                foreach (var elemAdyacente in elementosAdyacentes)
                {
                    try
                    {
                        Solid solidoAdyacente = EncofradoBaseHelper.ObtenerSolidoPrincipal(elemAdyacente);
                        if (solidoAdyacente == null) continue;

                        // Verificar intersección
                        Solid interseccion = BooleanOperationsUtils.ExecuteBooleanOperation(
                            solidoEncofrado,
                            solidoAdyacente,
                            BooleanOperationsType.Intersect);

                        if (interseccion != null && interseccion.Volume > TOLERANCIA_VOLUMEN)
                        {
                            // Aplicar diferencia
                            Solid resultado = BooleanOperationsUtils.ExecuteBooleanOperation(
                                solidoEncofrado,
                                solidoAdyacente,
                                BooleanOperationsType.Difference);

                            if (resultado != null && resultado.Volume > TOLERANCIA_VOLUMEN)
                            {
                                solidoEncofrado = resultado;
                                descuentos++;
                            }
                        }
                    }
                    catch { }
                }

                // Crear DirectShape con el sólido resultante
                DirectShape ds = DirectShape.CreateElement(
                    doc,
                    new ElementId(BuiltInCategory.OST_GenericModel));

                ds.SetShape(new GeometryObject[] { solidoEncofrado });
                ds.Name = $"Encofrado_{elementoOriginal.Id}_{descuentos}recortes";

                // Guardar metadata en comentarios
                var paramComentarios = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (paramComentarios != null && !paramComentarios.IsReadOnly)
                {
                    paramComentarios.Set(elementoOriginal.Id.ToString());
                }

                return ds;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extrae curvas de la cara principal de un DirectShape
        /// </summary>
        private static List<Curve> ExtraerCurvasDeDirectShape(DirectShape ds, PlanarFace caraOriginal)
        {
            try
            {
                // Obtener sólido del DirectShape
                Solid solido = EncofradoBaseHelper.ObtenerSolidoPrincipal(ds);
                if (solido == null) return null;

                // Buscar cara más similar a la original
                PlanarFace caraMejor = null;
                double similitudMaxima = 0;

                foreach (Face face in solido.Faces)
                {
                    if (face is PlanarFace pf)
                    {
                        // Calcular similitud basada en normal y área
                        double similitudNormal = pf.FaceNormal.DotProduct(caraOriginal.FaceNormal);
                        double similitudArea = Math.Min(pf.Area, caraOriginal.Area) /
                                             Math.Max(pf.Area, caraOriginal.Area);

                        double similitud = similitudNormal * similitudArea;

                        if (similitud > similitudMaxima)
                        {
                            similitudMaxima = similitud;
                            caraMejor = pf;
                        }
                    }
                }

                if (caraMejor == null) return null;

                // Extraer CurveLoops de la cara
                IList<CurveLoop> curveLoops = caraMejor.GetEdgesAsCurveLoops();
                if (curveLoops == null || curveLoops.Count == 0) return null;

                // Convertir a List<Curve>
                List<Curve> curves = new List<Curve>();

                foreach (CurveLoop curveLoop in curveLoops)
                {
                    foreach (Curve curve in curveLoop)
                    {
                        curves.Add(curve);
                    }
                }

                return curves;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Crea muro nativo usando curvas recortadas
        /// </summary>
        private static Wall CrearMuroNativoConCurvas(
            Document doc,
            List<Curve> curves,
            WallType wallType,
            DirectShape dsOriginal)
        {
            try
            {
                // Obtener nivel y altura desde el DirectShape
                BoundingBoxXYZ bbox = dsOriginal.get_BoundingBox(null);
                if (bbox == null) return null;

                double zMin = bbox.Min.Z;
                double zMax = bbox.Max.Z;

                // Obtener nivel base
                Level nivel = ObtenerNivelMasCercano(doc, zMin);
                if (nivel == null) return null;

                // Crear muro con las curvas
                Wall muro = Wall.Create(
                    doc,
                    curves,
                    wallType.Id,
                    nivel.Id,
                    true); // structural

                if (muro == null) return null;

                // Ajustar Base Offset
                double baseOffset = zMin - nivel.ProjectElevation;
                Parameter paramBaseOffset = muro.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                if (paramBaseOffset != null && !paramBaseOffset.IsReadOnly)
                {
                    paramBaseOffset.Set(baseOffset);
                }

                // Deshabilitar uniones automáticas
                try
                {
                    WallUtils.DisallowWallJoinAtEnd(muro, 0);
                    WallUtils.DisallowWallJoinAtEnd(muro, 1);
                }
                catch { }

                // Copiar comentarios del DirectShape
                var paramComentariosOrigen = dsOriginal.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                var paramComentariosDestino = muro.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);

                if (paramComentariosOrigen != null && paramComentariosOrigen.HasValue &&
                    paramComentariosDestino != null && !paramComentariosDestino.IsReadOnly)
                {
                    paramComentariosDestino.Set(paramComentariosOrigen.AsString());
                }

                return muro;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error creando muro nativo: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Crea suelo nativo usando curvas recortadas
        /// </summary>
        private static Floor CrearSueloNativoConCurvas(
            Document doc,
            List<Curve> curves,
            FloorType floorType,
            DirectShape dsOriginal)
        {
            try
            {
                // Crear CurveLoops desde las curvas
                List<CurveLoop> curveLoops = new List<CurveLoop>();

                // Intentar crear un CurveLoop con todas las curvas
                try
                {
                    CurveLoop loop = CurveLoop.Create(curves);
                    curveLoops.Add(loop);
                }
                catch
                {
                    // Si falla, intentar agrupar curvas manualmente
                    // (implementación básica, puede necesitar mejoras)
                    return null;
                }

                // Obtener nivel
                BoundingBoxXYZ bbox = dsOriginal.get_BoundingBox(null);
                if (bbox == null) return null;

                double zMin = bbox.Min.Z;
                Level nivel = ObtenerNivelMasCercano(doc, zMin);
                if (nivel == null) return null;

                // Crear suelo
                Floor suelo = Floor.Create(
                    doc,
                    curveLoops,
                    floorType.Id,
                    nivel.Id);

                if (suelo == null) return null;

                // Ajustar elevación
                double heightOffset = zMin - nivel.ProjectElevation;
                Parameter paramHeight = suelo.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                if (paramHeight != null && !paramHeight.IsReadOnly)
                {
                    paramHeight.Set(heightOffset);
                }

                // Copiar comentarios
                var paramComentariosOrigen = dsOriginal.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                var paramComentariosDestino = suelo.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);

                if (paramComentariosOrigen != null && paramComentariosOrigen.HasValue &&
                    paramComentariosDestino != null && !paramComentariosDestino.IsReadOnly)
                {
                    paramComentariosDestino.Set(paramComentariosOrigen.AsString());
                }

                return suelo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error creando suelo nativo: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Crea encofrado para cara cilíndrica (columnas circulares)
        /// </summary>
        private static Wall CrearEncofradoParaCaraCilindrica(
            Document doc,
            CylindricalFace caraCilindrica,
            WallType wallType,
            List<Element> elementosAdyacentes)
        {
            try
            {
                // Obtener altura de la cara cilíndrica
                BoundingBoxUV bbox = caraCilindrica.GetBoundingBox();
                double altura = bbox.Max.V - bbox.Min.V;

                // Usar nivel más cercano a la base
                var surface = caraCilindrica.GetSurface() as CylindricalSurface;
                if (surface == null) return null;

                XYZ origen = surface.Origin;
                Level nivel = ObtenerNivelMasCercano(doc, origen.Z);
                if (nivel == null) return null;

                // Usar GeometriaCurvaHelper para crear muro curvo con cortes
                Wall muroCurvo = GeometriaCurvaHelper.CrearEncofradoColumnaCircular(
                    doc,
                    caraCilindrica,
                    wallType,
                    nivel,
                    altura,
                    elementosAdyacentes);

                return muroCurvo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"❌ Error en CrearEncofradoParaCaraCilindrica: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Obtiene el espesor del tipo de muro o suelo
        /// </summary>
        private static double ObtenerEspesorDeTipo(WallType wallType, FloorType floorType)
        {
            if (wallType != null)
            {
                // Obtener espesor del muro
                CompoundStructure structure = wallType.GetCompoundStructure();
                if (structure != null)
                {
                    return structure.GetWidth();
                }
                // Fallback
                return 0.02; // 2cm
            }

            if (floorType != null)
            {
                // Obtener espesor del suelo
                CompoundStructure structure = floorType.GetCompoundStructure();
                if (structure != null)
                {
                    return structure.GetWidth();
                }
                // Fallback
                return 0.025; // 2.5cm
            }

            return 0.02; // Fallback genérico
        }

        /// <summary>
        /// Obtiene el nivel más cercano a una elevación dada
        /// </summary>
        private static Level ObtenerNivelMasCercano(Document doc, double elevacion)
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => Math.Abs(l.ProjectElevation - elevacion))
                .ToList();

            return levels.FirstOrDefault();
        }
    }
}
