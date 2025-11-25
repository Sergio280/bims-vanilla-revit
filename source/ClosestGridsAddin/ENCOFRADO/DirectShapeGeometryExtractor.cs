using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.ENCOFRADO
{
    /// <summary>
    /// Extrae datos geométricos de DirectShapes para crear Wall/Floor posteriormente
    /// Separa la extracción de datos de la creación de elementos
    /// </summary>
    public static class DirectShapeGeometryExtractor
    {
        /// <summary>
        /// Extrae todos los datos necesarios de un DirectShape para crear Wall/Floor después
        /// </summary>
        public static DirectShapeData ExtraerDatos(Document doc, DirectShape ds)
        {
            try
            {
                // 1. Extraer cara principal (la más grande)
                PlanarFace caraPrincipal = null;
                double areaMaxima = 0;

                Options opt = new Options();
                GeometryElement geom = ds.get_Geometry(opt);

                foreach (GeometryObject gObj in geom)
                {
                    if (gObj is Solid solid)
                    {
                        foreach (Face face in solid.Faces)
                        {
                            if (face is PlanarFace pf && face.Area > areaMaxima)
                            {
                                areaMaxima = face.Area;
                                caraPrincipal = pf;
                            }
                        }
                    }
                }

                if (caraPrincipal == null)
                {
                    System.Diagnostics.Debug.WriteLine($"DirectShape {ds.Id}: No se encontró cara planar");
                    return null;
                }

                // 2. Extraer contorno de la cara
                IList<CurveLoop> loops = caraPrincipal.GetEdgesAsCurveLoops();
                if (loops == null || loops.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"DirectShape {ds.Id}: No se encontraron curvas");
                    return null;
                }

                CurveLoop loopPrincipal = loops[0];

                // 3. Determinar orientación
                XYZ normal = caraPrincipal.FaceNormal;
                bool esVertical = Math.Abs(normal.Z) < 0.3; // Si Z < 0.3, es vertical (muro)

                // 4. Obtener nivel base del proyecto
                Level nivelBase = ObtenerNivelBase(doc);
                if (nivelBase == null)
                {
                    System.Diagnostics.Debug.WriteLine($"DirectShape {ds.Id}: No se encontró nivel base");
                    return null;
                }

                // 5. Extraer comentario (ID del elemento original)
                string comentario = "";
                var paramComentarios = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (paramComentarios != null && paramComentarios.HasValue)
                {
                    comentario = paramComentarios.AsString();
                }

                // 6. Crear objeto de datos
                var datos = new DirectShapeData
                {
                    DirectShapeId = ds.Id,
                    ContornoCompleto = loopPrincipal,
                    Normal = normal,
                    EsVertical = esVertical,
                    NivelBase = nivelBase,
                    Comentario = comentario,
                    Area = caraPrincipal.Area
                };

                // 7. Si es vertical (muro), extraer curva base y altura
                if (esVertical)
                {
                    // Extraer la curva más larga (será la base del muro)
                    Curve curvaBase = null;
                    double longitudMaxima = 0;

                    foreach (Curve curve in loopPrincipal)
                    {
                        if (curve.Length > longitudMaxima)
                        {
                            longitudMaxima = curve.Length;
                            curvaBase = curve;
                        }
                    }

                    if (curvaBase == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"DirectShape {ds.Id}: No se encontró curva base");
                        return null;
                    }

                    datos.CurvaBase = curvaBase;

                    // Calcular altura desde BoundingBox
                    BoundingBoxXYZ bbox = ds.get_BoundingBox(null);
                    if (bbox != null)
                    {
                        datos.Altura = bbox.Max.Z - bbox.Min.Z;
                    }
                    else
                    {
                        // Fallback: usar altura típica
                        datos.Altura = 3.0; // 3 metros por defecto
                        System.Diagnostics.Debug.WriteLine($"DirectShape {ds.Id}: BoundingBox null, usando altura por defecto");
                    }
                }

                return datos;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error extrayendo datos de DirectShape {ds.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Obtiene el nivel base del proyecto (el de menor elevación)
        /// </summary>
        private static Level ObtenerNivelBase(Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            ICollection<Element> levels = collector.OfClass(typeof(Level)).ToElements();

            Level nivelBase = null;
            double elevacionMinima = double.MaxValue;

            foreach (Element elem in levels)
            {
                Level level = elem as Level;
                if (level != null)
                {
                    double elevacion = level.Elevation;
                    if (elevacion < elevacionMinima)
                    {
                        elevacionMinima = elevacion;
                        nivelBase = level;
                    }
                }
            }

            return nivelBase;
        }

        /// <summary>
        /// Crea un Wall desde los datos extraídos
        /// </summary>
        public static Wall CrearMuro(Document doc, DirectShapeData datos, WallType wallType)
        {
            try
            {
                if (!datos.EsVertical || datos.CurvaBase == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Datos inválidos para crear muro desde DirectShape {datos.DirectShapeId}");
                    return null;
                }

                // Crear el muro
                Wall muro = Wall.Create(
                    doc,
                    datos.CurvaBase,
                    wallType.Id,
                    datos.NivelBase.Id,
                    datos.Altura,
                    0, // offset
                    false, // flip
                    false); // structural

                // Copiar comentario
                if (muro != null && !string.IsNullOrEmpty(datos.Comentario))
                {
                    var param = muro.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (param != null && !param.IsReadOnly)
                    {
                        param.Set(datos.Comentario);
                    }
                }

                return muro;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creando muro desde DirectShape {datos.DirectShapeId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Crea un Floor desde los datos extraídos
        /// </summary>
        public static Floor CrearSuelo(Document doc, DirectShapeData datos, FloorType floorType)
        {
            try
            {
                if (datos.EsVertical || datos.ContornoCompleto == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Datos inválidos para crear suelo desde DirectShape {datos.DirectShapeId}");
                    return null;
                }

                // Crear el suelo
                Floor suelo = Floor.Create(
                    doc,
                    new List<CurveLoop> { datos.ContornoCompleto },
                    floorType.Id,
                    datos.NivelBase.Id);

                // Copiar comentario
                if (suelo != null && !string.IsNullOrEmpty(datos.Comentario))
                {
                    var param = suelo.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (param != null && !param.IsReadOnly)
                    {
                        param.Set(datos.Comentario);
                    }
                }

                return suelo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creando suelo desde DirectShape {datos.DirectShapeId}: {ex.Message}");
                return null;
            }
        }
    }
}
