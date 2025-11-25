using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.ENCOFRADO
{
    /// <summary>
    /// Convierte DirectShapes (encofrados delgados) a Walls y Floors nativos
    /// usando las caras como referencia de ubicación
    /// </summary>
    public static class DirectShapeToWallFloorConverter
    {
        /// <summary>
        /// Convierte un DirectShape a Wall o Floor según su orientación
        /// </summary>
        public static Element ConvertToWallOrFloor(Document doc, DirectShape ds, WallType wallType, FloorType floorType)
        {
            try
            {
                // 1. Extraer la cara principal más grande
                PlanarFace caraPrincipal = ExtraerCaraPrincipal(ds);
                if (caraPrincipal == null)
                {
                    return null;
                }

                // 2. Determinar orientación
                XYZ normal = caraPrincipal.FaceNormal;
                bool esVertical = Math.Abs(normal.Z) < 0.3; // Si Z es pequeño, es vertical (muro)
                bool esHorizontal = Math.Abs(normal.Z) > 0.7; // Si Z es grande, es horizontal (suelo)

                Element nuevoElemento = null;

                if (esVertical)
                {
                    // CREAR MURO
                    nuevoElemento = CrearMuroDesdeDirectShape(doc, ds, caraPrincipal, wallType);
                }
                else if (esHorizontal)
                {
                    // CREAR SUELO
                    nuevoElemento = CrearSueloDesdeDirectShape(doc, ds, caraPrincipal, floorType);
                }

                return nuevoElemento;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error convirtiendo DirectShape {ds.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extrae la cara planar más grande de un DirectShape
        /// </summary>
        private static PlanarFace ExtraerCaraPrincipal(DirectShape ds)
        {
            Options opt = new Options();
            GeometryElement geom = ds.get_Geometry(opt);

            PlanarFace caraMaxima = null;
            double areaMaxima = 0;

            foreach (GeometryObject gObj in geom)
            {
                if (gObj is Solid solid)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace pf && face.Area > areaMaxima)
                        {
                            areaMaxima = face.Area;
                            caraMaxima = pf;
                        }
                    }
                }
            }

            return caraMaxima;
        }

        /// <summary>
        /// Crea un muro nativo desde un DirectShape vertical
        /// </summary>
        private static Wall CrearMuroDesdeDirectShape(Document doc, DirectShape ds, PlanarFace cara, WallType wallType)
        {
            try
            {
                // Obtener el contorno de la cara
                IList<CurveLoop> loops = cara.GetEdgesAsCurveLoops();
                if (loops == null || loops.Count == 0)
                {
                    return null;
                }

                // Usar el primer loop (contorno exterior)
                CurveLoop loop = loops[0];

                // Obtener la curva más larga del loop (será la línea base del muro)
                Curve curvaBase = null;
                double longitudMaxima = 0;

                foreach (Curve curve in loop)
                {
                    double longitud = curve.Length;
                    if (longitud > longitudMaxima)
                    {
                        longitudMaxima = longitud;
                        curvaBase = curve;
                    }
                }

                if (curvaBase == null)
                {
                    return null;
                }

                // Obtener altura del muro (desde el BoundingBox del DirectShape)
                BoundingBoxXYZ bbox = ds.get_BoundingBox(null);
                double altura = bbox.Max.Z - bbox.Min.Z;

                // Nivel base (nivel más bajo del proyecto)
                Level nivelBase = ObtenerNivelBase(doc);
                if (nivelBase == null)
                {
                    return null;
                }

                // Crear el muro
                Wall muro = Wall.Create(doc, curvaBase, wallType.Id, nivelBase.Id, altura, 0, false, false);

                // Copiar parámetro de comentarios si existe
                CopiarParametroComentarios(ds, muro);

                return muro;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creando muro: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Crea un suelo nativo desde un DirectShape horizontal
        /// </summary>
        private static Floor CrearSueloDesdeDirectShape(Document doc, DirectShape ds, PlanarFace cara, FloorType floorType)
        {
            try
            {
                // Obtener el contorno de la cara
                IList<CurveLoop> loops = cara.GetEdgesAsCurveLoops();
                if (loops == null || loops.Count == 0)
                {
                    return null;
                }

                // Crear CurveArray para el suelo
                CurveArray curveArray = new CurveArray();
                foreach (Curve curve in loops[0])
                {
                    curveArray.Append(curve);
                }

                // Nivel base
                Level nivelBase = ObtenerNivelBase(doc);
                if (nivelBase == null)
                {
                    return null;
                }

                // Crear el suelo usando Floor.Create (Revit 2025)
                Floor suelo = Floor.Create(doc, new List<CurveLoop> { loops[0] }, floorType.Id, nivelBase.Id);

                // Copiar parámetro de comentarios si existe
                CopiarParametroComentarios(ds, suelo);

                return suelo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creando suelo: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Obtiene el nivel base del proyecto
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
        /// Copia el parámetro de comentarios del DirectShape al elemento nativo
        /// </summary>
        private static void CopiarParametroComentarios(DirectShape origen, Element destino)
        {
            try
            {
                Parameter paramOrigenComentarios = origen.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                Parameter paramDestinoComentarios = destino.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);

                if (paramOrigenComentarios != null && paramOrigenComentarios.HasValue &&
                    paramDestinoComentarios != null && !paramDestinoComentarios.IsReadOnly)
                {
                    string comentario = paramOrigenComentarios.AsString();
                    paramDestinoComentarios.Set(comentario);
                }
            }
            catch
            {
                // Ignorar errores al copiar parámetros
            }
        }
    }
}
