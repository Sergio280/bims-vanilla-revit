using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.ENCOFRADO;

/// <summary>
/// Clase helper con métodos compartidos para el encofrado inteligente
/// </summary>
public static class EncofradoBaseHelper
{
    private const double TOLERANCIA_CONTACTO = 0.05; // 5cm de tolerancia para considerar contacto
    private const double ESPESOR_ENCOFRADO = 0.02; // 2cm de espesor estándar
    private const double MIN_VOLUMEN = 0.0001; // Volumen mínimo para considerar un sólido válido
    private const double TOLERANCIA_INTERSECCION = 0.1; // 10cm de tolerancia para intersecciones

    /// <summary>
    /// Obtiene el sólido principal de un elemento
    /// </summary>
    public static Solid ObtenerSolidoPrincipal(Element elemento)
    {
        var options = new Options
        {
            ComputeReferences = true,
            IncludeNonVisibleObjects = false,
            DetailLevel = ViewDetailLevel.Fine
        };

        var geoEl = elemento.get_Geometry(options);
        if (geoEl == null) return null;

        foreach (GeometryObject geoObj in geoEl)
        {
            if (geoObj is Solid solid && solid.Volume > MIN_VOLUMEN)
            {
                return solid;
            }
            if (geoObj is GeometryInstance geoInst)
            {
                var instGeo = geoInst.GetInstanceGeometry();
                foreach (GeometryObject instObj in instGeo)
                {
                    if (instObj is Solid solidInst && solidInst.Volume > MIN_VOLUMEN)
                    {
                        return solidInst;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Obtiene todos los elementos estructurales que podrían estar en contacto
    /// </summary>
    public static List<Element> ObtenerElementosAdyacentes(Document doc, Element elemento, double toleranciaExtra = 10)
    {
        var bbox = elemento.get_BoundingBox(null);
        if (bbox == null) return new List<Element>();

        // Expandir el BoundingBox para capturar elementos cercanos
        var min = new XYZ(
            bbox.Min.X - toleranciaExtra,
            bbox.Min.Y - toleranciaExtra,
            bbox.Min.Z - toleranciaExtra);
        var max = new XYZ(
            bbox.Max.X + toleranciaExtra,
            bbox.Max.Y + toleranciaExtra,
            bbox.Max.Z + toleranciaExtra);

        var outline = new Outline(min, max);
        var bbFilter = new BoundingBoxIntersectsFilter(outline);

        // Buscar todos los elementos estructurales cercanos
        var categorias = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_Stairs
        };

        var elementosAdyacentes = new List<Element>();
        
        foreach (var categoria in categorias)
        {
            var elementos = new FilteredElementCollector(doc)
                .OfCategory(categoria)
                .WherePasses(bbFilter)
                .Where(e => e.Id != elemento.Id) // Excluir el elemento actual
                .ToList();
            
            elementosAdyacentes.AddRange(elementos);
        }

        return elementosAdyacentes;
    }

    /// <summary>
    /// Crea encofrado para una cara, descontando las áreas de intersección con elementos adyacentes
    /// Versión simplificada y más robusta
    /// </summary>
    public static DirectShape CrearEncofradoInteligente(Document doc, PlanarFace cara,
        List<Element> elementosAdyacentes, string nombre, Element elementoOriginal = null, double espesor = ESPESOR_ENCOFRADO)
    {
        try
        {
            // Paso 1: Obtener sólido del elemento original para calcular dirección correcta
            Solid solidoElemento = elementoOriginal != null ?
                ObtenerSolidoPrincipal(elementoOriginal) : null;

            // Paso 2: Calcular dirección de extrusión HACIA AFUERA
            XYZ direccionExtrusion = cara.FaceNormal;
            if (solidoElemento != null)
            {
                direccionExtrusion = DireccionExtrusionHelper.ObtenerDireccionHaciaAfuera(
                    cara, solidoElemento);
            }

            // Paso 3: Crear el sólido de encofrado inicial (completo)
            var curveLoops = cara.GetEdgesAsCurveLoops();
            if (curveLoops == null || curveLoops.Count == 0) return null;

            Solid solidoEncofrado = GeometryCreationUtilities.CreateExtrusionGeometry(
                curveLoops, direccionExtrusion, espesor);

            if (solidoEncofrado == null || solidoEncofrado.Volume < MIN_VOLUMEN)
                return null;

            // Paso 2: Para cada elemento adyacente, intentar descontar la intersección
            int descuentosAplicados = 0;
            double volumenOriginal = solidoEncofrado.Volume;

            foreach (var elemento in elementosAdyacentes)
            {
                try
                {
                    var solidoAdyacente = ObtenerSolidoPrincipal(elemento);
                    if (solidoAdyacente == null) continue;

                    // Verificar si hay intersección real entre el encofrado y el elemento
                    Solid interseccion = null;
                    try
                    {
                        interseccion = BooleanOperationsUtils.ExecuteBooleanOperation(
                            solidoEncofrado, solidoAdyacente, BooleanOperationsType.Intersect);
                    }
                    catch { }

                    // Si hay intersección significativa, crear sólido de descuento
                    if (interseccion != null && interseccion.Volume > 0.00001)
                    {
                        // Crear un sólido de descuento basado en la proyección del elemento sobre la cara
                        var solidoDescuento = CrearSolidoDescuentoMejorado(
                            cara, solidoAdyacente, espesor * 3); // Extender más para asegurar penetración

                        if (solidoDescuento != null && solidoDescuento.Volume > MIN_VOLUMEN)
                        {
                            try
                            {
                                // Aplicar la diferencia booleana
                                var nuevoSolido = BooleanOperationsUtils.ExecuteBooleanOperation(
                                    solidoEncofrado, solidoDescuento, BooleanOperationsType.Difference);

                                // Solo aceptar el resultado si es válido y tiene volumen significativo
                                if (nuevoSolido != null && nuevoSolido.Volume > MIN_VOLUMEN && 
                                    nuevoSolido.Volume < volumenOriginal)
                                {
                                    TaskDialog.Show("Bug", "Bug de interseccción");
                                    solidoEncofrado = nuevoSolido;
                                    descuentosAplicados++;
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            // Paso 3: Crear el DirectShape con el sólido resultante
            if (solidoEncofrado != null && solidoEncofrado.Volume > MIN_VOLUMEN)
            {
                var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_Walls));
                ds.Name = descuentosAplicados > 0 ?
                    $"{nombre} ({descuentosAplicados} descuentos)" : nombre;
                ds.SetShape(new GeometryObject[] { solidoEncofrado });

                // Guardar el ID del elemento original en el parámetro Comentarios
                if (elementoOriginal != null)
                {
                    var paramComentarios = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (paramComentarios != null && !paramComentarios.IsReadOnly)
                    {
                        paramComentarios.Set($"{elementoOriginal.Id.Value}");
                    }
                }

                return ds;
            }
        }
        catch (Exception)
        {
            // Si falla todo, intentar crear encofrado básico
            try
            {
                // Calcular dirección correcta incluso para el fallback
                Solid solidoElemento = elementoOriginal != null ?
                    ObtenerSolidoPrincipal(elementoOriginal) : null;

                XYZ direccionExtrusion = cara.FaceNormal;
                if (solidoElemento != null)
                {
                    direccionExtrusion = DireccionExtrusionHelper.ObtenerDireccionHaciaAfuera(
                        cara, solidoElemento);
                }

                var curveLoops = cara.GetEdgesAsCurveLoops();
                if (curveLoops != null && curveLoops.Count > 0)
                {
                    var solidoBasico = GeometryCreationUtilities.CreateExtrusionGeometry(
                        curveLoops, direccionExtrusion, espesor);
                    
                    if (solidoBasico != null && solidoBasico.Volume > MIN_VOLUMEN)
                    {
                        var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                        ds.Name = nombre + " (Sin descuentos)";
                        ds.SetShape(new GeometryObject[] { solidoBasico });

                        // Guardar el ID del elemento original en el parámetro Comentarios
                        if (elementoOriginal != null)
                        {
                            var paramComentarios = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (paramComentarios != null && !paramComentarios.IsReadOnly)
                            {
                                paramComentarios.Set($"{elementoOriginal.Id.Value}");
                            }
                        }

                        return ds;
                    }
                }
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Crea un sólido de descuento mejorado basado en la intersección con el elemento adyacente
    /// </summary>
    private static Solid CrearSolidoDescuentoMejorado(PlanarFace caraEncofrado, 
        Solid solidoAdyacente, double profundidadExtrusion)
    {
        try
        {
            // Obtener el BoundingBox del sólido adyacente
            var bbox = solidoAdyacente.GetBoundingBox();
            var normalEncofrado = caraEncofrado.FaceNormal;
            
            // Obtener el centroide de la cara para determinar la posición
            var centroideCara = ObtenerCentroideCara(caraEncofrado);
            
            // Crear puntos para el perfil de descuento basado en la proyección del BoundingBox
            var puntos = new List<XYZ>();
            
            // Determinar la dirección principal del encofrado
            if (Math.Abs(normalEncofrado.X) > 0.7) // Cara perpendicular a X
            {
                // Proyectar el rectángulo YZ del BoundingBox
                double x = centroideCara.X;
                puntos.Add(new XYZ(x, bbox.Min.Y - 0.1, bbox.Min.Z - 0.1));
                puntos.Add(new XYZ(x, bbox.Max.Y + 0.1, bbox.Min.Z - 0.1));
                puntos.Add(new XYZ(x, bbox.Max.Y + 0.1, bbox.Max.Z + 0.1));
                puntos.Add(new XYZ(x, bbox.Min.Y - 0.1, bbox.Max.Z + 0.1));
            }
            else if (Math.Abs(normalEncofrado.Y) > 0.7) // Cara perpendicular a Y
            {
                // Proyectar el rectángulo XZ del BoundingBox
                double y = centroideCara.Y;
                puntos.Add(new XYZ(bbox.Min.X - 0.1, y, bbox.Min.Z - 0.1));
                puntos.Add(new XYZ(bbox.Max.X + 0.1, y, bbox.Min.Z - 0.1));
                puntos.Add(new XYZ(bbox.Max.X + 0.1, y, bbox.Max.Z + 0.1));
                puntos.Add(new XYZ(bbox.Min.X - 0.1, y, bbox.Max.Z + 0.1));
            }
            else if (Math.Abs(normalEncofrado.Z) > 0.7) // Cara perpendicular a Z
            {
                // Proyectar el rectángulo XY del BoundingBox
                double z = centroideCara.Z;
                puntos.Add(new XYZ(bbox.Min.X - 0.1, bbox.Min.Y - 0.1, z));
                puntos.Add(new XYZ(bbox.Max.X + 0.1, bbox.Min.Y - 0.1, z));
                puntos.Add(new XYZ(bbox.Max.X + 0.1, bbox.Max.Y + 0.1, z));
                puntos.Add(new XYZ(bbox.Min.X - 0.1, bbox.Max.Y + 0.1, z));
            }
            else
            {
                // Para caras inclinadas, usar toda la proyección del BoundingBox
                return CrearSolidoDescuentoDesdeBoundingBoxCompleto(
                    caraEncofrado, bbox, profundidadExtrusion);
            }

            if (puntos.Count == 4)
            {
                // Crear las líneas del perfil
                var curvas = new List<Curve>();
                for (int i = 0; i < puntos.Count; i++)
                {
                    var p1 = puntos[i];
                    var p2 = puntos[(i + 1) % puntos.Count];
                    
                    try
                    {
                        var linea = Line.CreateBound(p1, p2);
                        if (linea.Length > 0.001)
                        {
                            curvas.Add(linea);
                        }
                    }
                    catch { }
                }

                if (curvas.Count >= 3)
                {
                    try
                    {
                        var curveLoop = CurveLoop.Create(curvas);
                        var curveLoops = new List<CurveLoop> { curveLoop };
                        
                        // Extruir en dirección opuesta a la normal del encofrado
                        return GeometryCreationUtilities.CreateExtrusionGeometry(
                            curveLoops, -normalEncofrado, profundidadExtrusion);
                    }
                    catch { }
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Crea un sólido de descuento usando todo el BoundingBox (para casos complejos)
    /// </summary>
    private static Solid CrearSolidoDescuentoDesdeBoundingBoxCompleto(
        PlanarFace caraEncofrado, BoundingBoxXYZ bbox, double profundidad)
    {
        try
        {
            // Expandir ligeramente el BoundingBox
            var min = new XYZ(
                bbox.Min.X - 0.05,
                bbox.Min.Y - 0.05,
                bbox.Min.Z - 0.05);
            var max = new XYZ(
                bbox.Max.X + 0.05,
                bbox.Max.Y + 0.05,
                bbox.Max.Z + 0.05);

            // Crear las 8 esquinas del box
            var p1 = new XYZ(min.X, min.Y, min.Z);
            var p2 = new XYZ(max.X, min.Y, min.Z);
            var p3 = new XYZ(max.X, max.Y, min.Z);
            var p4 = new XYZ(min.X, max.Y, min.Z);

            // Crear la cara inferior como CurveLoop
            var curvas = new List<Curve>
            {
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p4),
                Line.CreateBound(p4, p1)
            };

            var curveLoop = CurveLoop.Create(curvas);
            var curveLoops = new List<CurveLoop> { curveLoop };

            // Crear sólido por extrusión
            var altura = max.Z - min.Z;
            return GeometryCreationUtilities.CreateExtrusionGeometry(
                curveLoops, XYZ.BasisZ, altura);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Obtiene el centroide aproximado de una cara
    /// </summary>
    private static XYZ ObtenerCentroideCara(PlanarFace cara)
    {
        var edgeLoops = cara.GetEdgesAsCurveLoops();
        if (edgeLoops == null || edgeLoops.Count == 0) 
            return XYZ.Zero;

        var puntos = new List<XYZ>();
        
        // Usar solo el primer loop (normalmente el exterior)
        var loopPrincipal = edgeLoops.First();
        foreach (var curve in loopPrincipal)
        {
            puntos.Add(curve.GetEndPoint(0));
            puntos.Add(curve.GetEndPoint(1));
        }

        if (puntos.Count == 0) return XYZ.Zero;

        // Calcular el centroide
        var suma = puntos.Aggregate(XYZ.Zero, (acc, p) => acc + p);
        return suma / puntos.Count;
    }

    /// <summary>
    /// Método alternativo: Crea encofrado con recortes basados en intersecciones directas
    /// </summary>
    public static DirectShape CrearEncofradoConRecortes(Document doc, PlanarFace cara,
        List<Element> elementosAdyacentes, string nombre, double espesor = ESPESOR_ENCOFRADO)
    {
        try
        {
            // Crear el encofrado base
            var curveLoops = cara.GetEdgesAsCurveLoops();
            if (curveLoops == null || curveLoops.Count == 0) return null;

            var solidoEncofrado = GeometryCreationUtilities.CreateExtrusionGeometry(
                curveLoops, cara.FaceNormal, espesor);

            if (solidoEncofrado == null) return null;

            // Para cada elemento adyacente
            foreach (var elemento in elementosAdyacentes)
            {
                var solidoAdyacente = ObtenerSolidoPrincipal(elemento);
                if (solidoAdyacente == null) continue;

                try
                {
                    // Intentar resta directa
                    var resultado = BooleanOperationsUtils.ExecuteBooleanOperation(
                        solidoEncofrado, solidoAdyacente, BooleanOperationsType.Difference);

                    if (resultado != null && resultado.Volume > MIN_VOLUMEN)
                    {
                        solidoEncofrado = resultado;
                    }
                }
                catch
                {
                    // Si falla la resta directa, intentar con un sólido expandido
                    try
                    {
                        var bboxAdyacente = solidoAdyacente.GetBoundingBox();
                        var solidoExpandido = CrearSolidoExpandido(bboxAdyacente, 0.05);
                        
                        if (solidoExpandido != null)
                        {
                            var resultado = BooleanOperationsUtils.ExecuteBooleanOperation(
                                solidoEncofrado, solidoExpandido, BooleanOperationsType.Difference);
                            
                            if (resultado != null && resultado.Volume > MIN_VOLUMEN)
                            {
                                solidoEncofrado = resultado;
                            }
                        }
                    }
                    catch { }
                }
            }

            // Crear el DirectShape
            if (solidoEncofrado != null && solidoEncofrado.Volume > MIN_VOLUMEN)
            {
                var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                ds.Name = nombre;
                ds.SetShape(new GeometryObject[] { solidoEncofrado });
                return ds;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Crea un sólido expandido desde un BoundingBox
    /// </summary>
    private static Solid CrearSolidoExpandido(BoundingBoxXYZ bbox, double expansion)
    {
        try
        {
            var min = new XYZ(
                bbox.Min.X - expansion,
                bbox.Min.Y - expansion,
                bbox.Min.Z - expansion);
            var max = new XYZ(
                bbox.Max.X + expansion,
                bbox.Max.Y + expansion,
                bbox.Max.Z + expansion);

            // Crear las 8 esquinas del box
            var p1 = new XYZ(min.X, min.Y, min.Z);
            var p2 = new XYZ(max.X, min.Y, min.Z);
            var p3 = new XYZ(max.X, max.Y, min.Z);
            var p4 = new XYZ(min.X, max.Y, min.Z);
            var p5 = new XYZ(min.X, min.Y, max.Z);
            var p6 = new XYZ(max.X, min.Y, max.Z);
            var p7 = new XYZ(max.X, max.Y, max.Z);
            var p8 = new XYZ(min.X, max.Y, max.Z);

            // Crear las caras como CurveLoops
            var faces = new List<CurveLoop>();

            // Cara inferior
            faces.Add(CurveLoop.Create(new List<Curve> {
                Line.CreateBound(p1, p2),
                Line.CreateBound(p2, p3),
                Line.CreateBound(p3, p4),
                Line.CreateBound(p4, p1)
            }));

            // Crear sólido por extrusión
            var height = max.Z - min.Z;
            return GeometryCreationUtilities.CreateExtrusionGeometry(
                faces, XYZ.BasisZ, height);
        }
        catch
        {
            return null;
        }
    }






/// <summary>
/// Crea muros aprovechando la geometría de un DirectShape con descuentos aplicados
/// </summary>
public static List<Wall> CrearMurosDesdeDirectShapeConDescuentos(Document doc, DirectShape ds,
    Level nivel, WallType tipoMuro, string nombreBase)
    {
        var murosCreados = new List<Wall>();

        try
        {
            // Paso 1: Extraer el sólido del DirectShape
            var solido = ExtraerSolidoDeDirectShape(ds);
            if (solido == null || solido.Volume < MIN_VOLUMEN)
            {
                System.Diagnostics.Debug.WriteLine("❌ No se pudo extraer sólido válido del DirectShape");
                return murosCreados;
            }

            System.Diagnostics.Debug.WriteLine($"✅ Sólido extraído: Volumen={solido.Volume:F6}");

            // Paso 2: Identificar caras verticales con descuentos
            var carasVerticalesConDescuentos = ExtraerCarasVerticalesConGeometria(solido);

            System.Diagnostics.Debug.WriteLine($"📊 Caras verticales encontradas: {carasVerticalesConDescuentos.Count}");

            // Paso 3: Para cada cara vertical, crear un muro que respete los descuentos
            int contador = 1;
            foreach (var caraInfo in carasVerticalesConDescuentos)
            {
                try
                {
                    var muro = CrearMuroDesdeCaraConDescuentos(
                        doc, caraInfo, nivel, tipoMuro, $"{nombreBase}_{contador}");

                    if (muro != null)
                    {
                        murosCreados.Add(muro);
                        System.Diagnostics.Debug.WriteLine($"✅ Muro {contador} creado: ID={muro.Id}");
                        contador++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error creando muro {contador}: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"\n🎯 Total muros creados: {murosCreados.Count}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error en CrearMurosDesdeDirectShapeConDescuentos: {ex.Message}");
        }

        return murosCreados;
    }

    /// <summary>
    /// Extrae el sólido principal de un DirectShape
    /// </summary>
    private static Solid ExtraerSolidoDeDirectShape(DirectShape ds)
    {
        try
        {
            var options = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            var geoElem = ds.get_Geometry(options);
            if (geoElem == null) return null;

            foreach (GeometryObject geoObj in geoElem)
            {
                if (geoObj is Solid solid && solid.Volume > MIN_VOLUMEN)
                {
                    return solid;
                }

                if (geoObj is GeometryInstance gi)
                {
                    var inst = gi.GetInstanceGeometry();
                    foreach (GeometryObject sub in inst)
                    {
                        if (sub is Solid s2 && s2.Volume > MIN_VOLUMEN)
                        {
                            return s2;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error extrayendo sólido: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Información de una cara vertical con su geometría y contexto
    /// </summary>
    private class CaraVerticalInfo
    {
        public PlanarFace Cara { get; set; }
        public List<CurveLoop> EdgeLoops { get; set; }
        public XYZ Normal { get; set; }
        public BoundingBoxUV BoundingBox { get; set; }
        public double AlturaPromedio { get; set; }
        public bool TieneGeometriaCompleja { get; set; }
    }

    /// <summary>
    /// Extrae información detallada de las caras verticales del sólido
    /// </summary>
    private static List<CaraVerticalInfo> ExtraerCarasVerticalesConGeometria(Solid solido)
    {
        var carasInfo = new List<CaraVerticalInfo>();

        try
        {
            var faces = solido.Faces;
            for (int i = 0; i < faces.Size; i++)
            {
                Face face = faces.get_Item(i);

                if (face is PlanarFace pf)
                {
                    var normal = pf.FaceNormal;
                    bool esVertical = Math.Abs(normal.Z) < TOLERANCIA_CONTACTO;

                    if (esVertical)
                    {
                        var edgeLoops = pf.GetEdgesAsCurveLoops();
                        var bbox = pf.GetBoundingBox();

                        var info = new CaraVerticalInfo
                        {
                            Cara = pf,
                            EdgeLoops = edgeLoops.Cast<CurveLoop>().ToList(),
                            Normal = normal,
                            BoundingBox = bbox,
                            AlturaPromedio = bbox.Max.V - bbox.Min.V,
                            TieneGeometriaCompleja = edgeLoops.Count > 1 // Tiene huecos/descuentos
                        };

                        carasInfo.Add(info);

                        System.Diagnostics.Debug.WriteLine(
                            $"  Cara {i}: Loops={edgeLoops.Count}, " +
                            $"Altura={info.AlturaPromedio:F2}m, " +
                            $"Compleja={info.TieneGeometriaCompleja}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error extrayendo caras: {ex.Message}");
        }

        return carasInfo;
    }

    /// <summary>
    /// Crea un muro desde una cara que puede tener descuentos (geometría compleja)
    /// </summary>
    private static Wall CrearMuroDesdeCaraConDescuentos(Document doc, CaraVerticalInfo caraInfo,
        Level nivel, WallType tipoMuro, string nombre)
    {
        try
        {
            // Estrategia: Si la cara tiene geometría compleja (descuentos),
            // crear el muro con perfil personalizado

            if (caraInfo.TieneGeometriaCompleja)
            {
                System.Diagnostics.Debug.WriteLine($"  🔧 Cara compleja detectada, creando muro con perfil");
                return CrearMuroConPerfilComplejo(doc, caraInfo, nivel, tipoMuro, nombre);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"  🔧 Cara simple, creando muro estándar");
                return CrearMuroSimpleDesdeCaraInfo(doc, caraInfo, nivel, tipoMuro, nombre);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error en CrearMuroDesdeCaraConDescuentos: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Crea un muro simple desde una cara sin descuentos
    /// </summary>
    private static Wall CrearMuroSimpleDesdeCaraInfo(Document doc, CaraVerticalInfo caraInfo,
        Level nivel, WallType tipoMuro, string nombre)
    {
        try
        {
            // Obtener el primer EdgeLoop (contorno exterior)
            if (caraInfo.EdgeLoops == null || caraInfo.EdgeLoops.Count == 0)
                return null;

            var loopPrincipal = caraInfo.EdgeLoops[0];

            // Buscar la curva base (más baja y horizontal)
            Curve curvaBase = null;
            double menorZ = double.MaxValue;

            foreach (var curva in loopPrincipal)
            {
                var p0 = curva.GetEndPoint(0);
                var p1 = curva.GetEndPoint(1);
                double minZ = Math.Min(p0.Z, p1.Z);
                double maxZ = Math.Max(p0.Z, p1.Z);

                bool esHorizontal = Math.Abs(maxZ - minZ) < TOLERANCIA_CONTACTO;

                if (esHorizontal && minZ < menorZ)
                {
                    menorZ = minZ;
                    curvaBase = curva;
                }
            }

            if (curvaBase == null)
            {
                curvaBase = loopPrincipal.First();
            }

            // Proyectar al nivel
            var p0Orig = curvaBase.GetEndPoint(0);
            var p1Orig = curvaBase.GetEndPoint(1);
            var p0Proj = new XYZ(p0Orig.X, p0Orig.Y, nivel.Elevation);
            var p1Proj = new XYZ(p1Orig.X, p1Orig.Y, nivel.Elevation);

            double longitud = p0Proj.DistanceTo(p1Proj);
            if (longitud < 0.01) return null;

            var lineaBase = Line.CreateBound(p0Proj, p1Proj);
            double altura = caraInfo.AlturaPromedio;

            // Crear muro
            var muro = Wall.Create(doc, lineaBase, tipoMuro.Id, nivel.Id, altura, 0, false, false);

            if (muro != null)
            {
                var paramComentario = muro.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (paramComentario != null && !paramComentario.IsReadOnly)
                {
                    paramComentario.Set(nombre);
                }
            }

            return muro;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error en CrearMuroSimpleDesdeCaraInfo: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Crea un muro con perfil complejo para representar descuentos
    /// NOTA: Revit tiene limitaciones para muros con perfiles complejos.
    /// Como alternativa, crea múltiples muros segmentados.
    /// </summary>
    private static Wall CrearMuroConPerfilComplejo(Document doc, CaraVerticalInfo caraInfo,
        Level nivel, WallType tipoMuro, string nombre)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"  ⚠️ Perfil complejo detectado con {caraInfo.EdgeLoops.Count} loops");

            // ESTRATEGIA ALTERNATIVA: Dividir en segmentos verticales
            // Para cada segmento horizontal del contorno exterior, crear un muro

            var loopExterior = caraInfo.EdgeLoops[0];
            var curvasHorizontales = new List<Curve>();

            // Identificar curvas horizontales en la base
            double minZ = caraInfo.BoundingBox.Min.V;

            foreach (var curva in loopExterior)
            {
                var p0 = curva.GetEndPoint(0);
                var p1 = curva.GetEndPoint(1);
                double avgZ = (p0.Z + p1.Z) / 2;

                bool estaEnBase = Math.Abs(avgZ - minZ) < TOLERANCIA_CONTACTO;
                bool esHorizontal = Math.Abs(p0.Z - p1.Z) < TOLERANCIA_CONTACTO;

                if (estaEnBase && esHorizontal && curva.Length > 0.1)
                {
                    curvasHorizontales.Add(curva);
                }
            }

            System.Diagnostics.Debug.WriteLine($"  📏 Curvas horizontales en base: {curvasHorizontales.Count}");

            // Crear muro con la curva horizontal más larga
            if (curvasHorizontales.Count > 0)
            {
                var curvaBase = curvasHorizontales.OrderByDescending(c => c.Length).First();

                var p0 = curvaBase.GetEndPoint(0);
                var p1 = curvaBase.GetEndPoint(1);
                var p0Proj = new XYZ(p0.X, p0.Y, nivel.Elevation);
                var p1Proj = new XYZ(p1.X, p1.Y, nivel.Elevation);

                var lineaBase = Line.CreateBound(p0Proj, p1Proj);
                double altura = caraInfo.AlturaPromedio;

                var muro = Wall.Create(doc, lineaBase, tipoMuro.Id, nivel.Id, altura, 0, false, false);

                if (muro != null)
                {
                    var paramComentario = muro.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (paramComentario != null && !paramComentario.IsReadOnly)
                    {
                        int numDescuentos = caraInfo.EdgeLoops.Count - 1;
                        paramComentario.Set($"{nombre} ({numDescuentos} descuentos)");
                    }

                    System.Diagnostics.Debug.WriteLine($"  ✅ Muro con perfil complejo creado");
                }

                return muro;
            }

            // Si no hay curvas horizontales, usar método simple como fallback
            return CrearMuroSimpleDesdeCaraInfo(doc, caraInfo, nivel, tipoMuro, nombre);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error en CrearMuroConPerfilComplejo: {ex.Message}");
            return null;
        }
    }







}
