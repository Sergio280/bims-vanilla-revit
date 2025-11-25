using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace ClosestGridsAddinVANILLA.ENCOFRADO
{
    /// <summary>
    /// Herramienta para analizar elementos creados por BLIMTAR
    /// Extrae información detallada de geometría, parámetros y relaciones
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AnalizadorBLIMTAR : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Solicitar al usuario que seleccione elementos a analizar
                TaskDialog.Show("Analizador BLIMTAR",
                    "Selecciona los elementos de encofrado creados por BLIMTAR.\n" +
                    "Luego presiona ESC o clic derecho para finalizar la selección.");

                IList<Reference> selectedRefs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    "Selecciona elementos de encofrado creados por BLIMTAR");

                if (selectedRefs == null || selectedRefs.Count == 0)
                {
                    TaskDialog.Show("Error", "No se seleccionaron elementos.");
                    return Result.Cancelled;
                }

                // Analizar cada elemento seleccionado
                StringBuilder reporte = new StringBuilder();
                reporte.AppendLine("=".PadRight(80, '='));
                reporte.AppendLine("ANÁLISIS DE ELEMENTOS BLIMTAR");
                reporte.AppendLine("=".PadRight(80, '='));
                reporte.AppendLine($"Fecha: {DateTime.Now}");
                reporte.AppendLine($"Elementos analizados: {selectedRefs.Count}");
                reporte.AppendLine();

                int contador = 1;
                foreach (Reference refElem in selectedRefs)
                {
                    Element elem = doc.GetElement(refElem);
                    reporte.AppendLine($"\n{'▬'.ToString().PadRight(80, '▬')}");
                    reporte.AppendLine($"ELEMENTO {contador}/{selectedRefs.Count}");
                    reporte.AppendLine($"{'▬'.ToString().PadRight(80, '▬')}\n");

                    AnalizarElemento(elem, doc, reporte);
                    contador++;
                }

                // Guardar reporte en archivo
                string rutaDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string nombreArchivo = $"Analisis_BLIMTAR_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string rutaCompleta = System.IO.Path.Combine(rutaDesktop, nombreArchivo);

                System.IO.File.WriteAllText(rutaCompleta, reporte.ToString());

                // Mostrar resultado
                TaskDialog td = new TaskDialog("Análisis Completado")
                {
                    MainInstruction = "Análisis BLIMTAR completado",
                    MainContent = $"Se analizaron {selectedRefs.Count} elementos.\n\n" +
                                  $"Reporte guardado en:\n{rutaCompleta}",
                    CommonButtons = TaskDialogCommonButtons.Ok
                };
                td.Show();

                // Abrir el archivo
                System.Diagnostics.Process.Start("notepad.exe", rutaCompleta);

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"Error en AnalizadorBLIMTAR: {ex.Message}\n{ex.StackTrace}";
                return Result.Failed;
            }
        }

        private void AnalizarElemento(Element elem, Document doc, StringBuilder reporte)
        {
            // INFORMACIÓN BÁSICA
            reporte.AppendLine("┌─ INFORMACIÓN BÁSICA");
            reporte.AppendLine($"│  ElementId: {elem.Id}");
            reporte.AppendLine($"│  Tipo de Elemento: {elem.GetType().Name}");
            reporte.AppendLine($"│  Categoría: {elem.Category?.Name ?? "N/A"}");
            reporte.AppendLine($"│  Nombre: {elem.Name}");

            if (elem is Wall wall)
            {
                reporte.AppendLine($"│  WallType: {wall.WallType.Name}");
                reporte.AppendLine($"│  Ancho: {UnitUtils.ConvertFromInternalUnits(wall.Width, UnitTypeId.Millimeters):F2} mm");
            }
            else if (elem is Floor floor)
            {
                reporte.AppendLine($"│  FloorType: {floor.FloorType.Name}");
            }
            else if (elem is DirectShape ds)
            {
                reporte.AppendLine($"│  DirectShape Category: {ds.Category.Name}");
            }

            // GEOMETRÍA
            reporte.AppendLine("│");
            reporte.AppendLine("├─ GEOMETRÍA");

            Options geomOptions = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = false
            };

            GeometryElement geomElem = elem.get_Geometry(geomOptions);
            if (geomElem != null)
            {
                List<Solid> solidos = ExtraerSolidos(geomElem);

                reporte.AppendLine($"│  Sólidos encontrados: {solidos.Count}");

                double volumenTotal = 0;
                double areaTotal = 0;

                for (int i = 0; i < solidos.Count; i++)
                {
                    Solid solido = solidos[i];
                    if (solido != null && solido.Volume > 0)
                    {
                        double volumen = UnitUtils.ConvertFromInternalUnits(solido.Volume, UnitTypeId.CubicMeters);
                        double area = UnitUtils.ConvertFromInternalUnits(solido.SurfaceArea, UnitTypeId.SquareMeters);

                        volumenTotal += volumen;
                        areaTotal += area;

                        reporte.AppendLine($"│  ");
                        reporte.AppendLine($"│  Sólido {i + 1}:");
                        reporte.AppendLine($"│    - Volumen: {volumen:F6} m³");
                        reporte.AppendLine($"│    - Área superficie: {area:F4} m²");
                        reporte.AppendLine($"│    - Caras: {solido.Faces.Size}");
                        reporte.AppendLine($"│    - Aristas: {solido.Edges.Size}");

                        // BoundingBox
                        BoundingBoxXYZ bbox = solido.GetBoundingBox();
                        if (bbox != null)
                        {
                            XYZ min = bbox.Min;
                            XYZ max = bbox.Max;
                            reporte.AppendLine($"│    - BoundingBox Min: ({min.X:F3}, {min.Y:F3}, {min.Z:F3})");
                            reporte.AppendLine($"│    - BoundingBox Max: ({max.X:F3}, {max.Y:F3}, {max.Z:F3})");

                            double ancho = Math.Abs(max.X - min.X);
                            double largo = Math.Abs(max.Y - min.Y);
                            double alto = Math.Abs(max.Z - min.Z);
                            reporte.AppendLine($"│    - Dimensiones BBox: {UnitUtils.ConvertFromInternalUnits(ancho, UnitTypeId.Millimeters):F2} x " +
                                             $"{UnitUtils.ConvertFromInternalUnits(largo, UnitTypeId.Millimeters):F2} x " +
                                             $"{UnitUtils.ConvertFromInternalUnits(alto, UnitTypeId.Millimeters):F2} mm");
                        }
                    }
                }

                reporte.AppendLine($"│  ");
                reporte.AppendLine($"│  TOTAL - Volumen: {volumenTotal:F6} m³");
                reporte.AppendLine($"│  TOTAL - Área: {areaTotal:F4} m²");
            }

            // PARÁMETROS
            reporte.AppendLine("│");
            reporte.AppendLine("├─ PARÁMETROS");

            ParameterSet paramSet = elem.Parameters;
            List<Parameter> parametrosOrdenados = new List<Parameter>();

            foreach (Parameter param in paramSet)
            {
                parametrosOrdenados.Add(param);
            }

            parametrosOrdenados = parametrosOrdenados.OrderBy(p => p.Definition.Name).ToList();

            foreach (Parameter param in parametrosOrdenados)
            {
                if (param.HasValue)
                {
                    string valor = ObtenerValorParametro(param);
                    if (!string.IsNullOrWhiteSpace(valor))
                    {
                        reporte.AppendLine($"│  {param.Definition.Name}: {valor}");
                    }
                }
            }

            // PARÁMETROS SOSPECHOSOS DE RELACIÓN (Host, GUID, Area, etc)
            reporte.AppendLine("│");
            reporte.AppendLine("├─ PARÁMETROS CLAVE (Host, GUID, Area)");

            bool encontroParametrosClave = false;
            foreach (Parameter param in paramSet)
            {
                string nombre = param.Definition.Name.ToLower();
                if (nombre.Contains("host") || nombre.Contains("guid") ||
                    nombre.Contains("area") || nombre.Contains("área") ||
                    nombre.Contains("blim") || nombre.Contains("elemento") ||
                    nombre.Contains("structural") || nombre.Contains("estructural"))
                {
                    if (param.HasValue)
                    {
                        string valor = ObtenerValorParametro(param);
                        reporte.AppendLine($"│  ★ {param.Definition.Name}: {valor}");
                        encontroParametrosClave = true;
                    }
                }
            }

            if (!encontroParametrosClave)
            {
                reporte.AppendLine($"│  (No se encontraron parámetros clave)");
            }

            // LOCATION
            reporte.AppendLine("│");
            reporte.AppendLine("├─ UBICACIÓN");

            Location location = elem.Location;
            if (location is LocationPoint lp)
            {
                XYZ punto = lp.Point;
                reporte.AppendLine($"│  LocationPoint: ({punto.X:F3}, {punto.Y:F3}, {punto.Z:F3})");
                reporte.AppendLine($"│  Rotación: {lp.Rotation * 180 / Math.PI:F2}°");
            }
            else if (location is LocationCurve lc)
            {
                Curve curva = lc.Curve;
                reporte.AppendLine($"│  LocationCurve: {curva.GetType().Name}");
                reporte.AppendLine($"│  Punto Inicio: ({curva.GetEndPoint(0).X:F3}, {curva.GetEndPoint(0).Y:F3}, {curva.GetEndPoint(0).Z:F3})");
                reporte.AppendLine($"│  Punto Fin: ({curva.GetEndPoint(1).X:F3}, {curva.GetEndPoint(1).Y:F3}, {curva.GetEndPoint(1).Z:F3})");
                reporte.AppendLine($"│  Longitud: {UnitUtils.ConvertFromInternalUnits(curva.Length, UnitTypeId.Meters):F3} m");
            }

            // ELEMENTOS CERCANOS (posibles hosts)
            reporte.AppendLine("│");
            reporte.AppendLine("└─ ELEMENTOS CERCANOS (posibles hosts)");

            BoundingBoxXYZ elemBBox = elem.get_BoundingBox(null);
            if (elemBBox != null)
            {
                // Expandir BBox para búsqueda
                XYZ offset = new XYZ(1, 1, 1); // 1 pie de offset
                elemBBox.Min = elemBBox.Min - offset;
                elemBBox.Max = elemBBox.Max + offset;

                Outline outline = new Outline(elemBBox.Min, elemBBox.Max);
                BoundingBoxIntersectsFilter bboxFilter = new BoundingBoxIntersectsFilter(outline);

                FilteredElementCollector collector = new FilteredElementCollector(doc)
                    .WherePasses(bboxFilter)
                    .WhereElementIsNotElementType();

                List<Element> cercanos = new List<Element>();
                foreach (Element e in collector)
                {
                    if (e.Id != elem.Id &&
                        (e is Wall || e is Floor || e is FamilyInstance))
                    {
                        cercanos.Add(e);
                    }
                    // Agregar escaleras por categoría
                    else if (e.Id != elem.Id && e.Category?.Name == "Escaleras")
                    {
                        cercanos.Add(e);
                    }
                }

                if (cercanos.Count > 0)
                {
                    reporte.AppendLine($"   Encontrados {cercanos.Count} elementos estructurales cercanos:");
                    foreach (Element cercano in cercanos.Take(10)) // Limitar a 10
                    {
                        reporte.AppendLine($"   - [{cercano.Id}] {cercano.Category.Name}: {cercano.Name}");
                    }
                    if (cercanos.Count > 10)
                    {
                        reporte.AppendLine($"   ... y {cercanos.Count - 10} más");
                    }
                }
                else
                {
                    reporte.AppendLine($"   (No se encontraron elementos estructurales cercanos)");
                }
            }

            reporte.AppendLine();
        }

        private List<Solid> ExtraerSolidos(GeometryElement geomElem)
        {
            List<Solid> solidos = new List<Solid>();

            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid solid)
                {
                    if (solid.Volume > 0)
                    {
                        solidos.Add(solid);
                    }
                }
                else if (geomObj is GeometryInstance geomInst)
                {
                    GeometryElement instGeom = geomInst.GetInstanceGeometry();
                    solidos.AddRange(ExtraerSolidos(instGeom));
                }
            }

            return solidos;
        }

        private string ObtenerValorParametro(Parameter param)
        {
            try
            {
                switch (param.StorageType)
                {
                    case StorageType.String:
                        return param.AsString() ?? "";

                    case StorageType.Integer:
                        return param.AsInteger().ToString();

                    case StorageType.Double:
                        double valor = param.AsDouble();

                        // Intentar convertir a unidades apropiadas
                        ForgeTypeId unitType = param.GetUnitTypeId();
                        if (unitType != null)
                        {
                            try
                            {
                                // Intentar convertir a metros para longitudes
                                if (unitType == SpecTypeId.Length)
                                {
                                    double metros = UnitUtils.ConvertFromInternalUnits(valor, UnitTypeId.Meters);
                                    return $"{metros:F3} m ({valor:F3} ft)";
                                }
                                else if (unitType == SpecTypeId.Area)
                                {
                                    double m2 = UnitUtils.ConvertFromInternalUnits(valor, UnitTypeId.SquareMeters);
                                    return $"{m2:F4} m² ({valor:F4} sf)";
                                }
                                else if (unitType == SpecTypeId.Volume)
                                {
                                    double m3 = UnitUtils.ConvertFromInternalUnits(valor, UnitTypeId.CubicMeters);
                                    return $"{m3:F6} m³ ({valor:F6} cf)";
                                }
                            }
                            catch
                            {
                                // Si falla conversión, usar valor crudo
                            }
                        }

                        return $"{valor:F6}";

                    case StorageType.ElementId:
                        ElementId id = param.AsElementId();
                        if (id != null && id != ElementId.InvalidElementId)
                        {
                            return $"ElementId: {id}";
                        }
                        return "";

                    default:
                        return "";
                }
            }
            catch
            {
                return "(Error al leer valor)";
            }
        }
    }
}
