using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using Nice3point.Revit.Toolkit.External;

namespace ClosestGridsAddinVANILLA.Commands
{
    /// <summary>
    /// Comando para dividir un DirectShape en todas las piezas individuales que contenga
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SplitDirectShapeCommand : ExternalCommand, IExternalCommand
    {
        public override void Execute()
        {
            var uiApp = ExternalCommandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            var doc = uiDoc.Document;

            try
            {
                // Seleccionar el DirectShape
                var reference = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new DirectShapeSelectionFilter(),
                    "Seleccione un DirectShape para dividir en piezas");

                if (reference == null)
                {
                    TaskDialog.Show("Cancelado", "No se seleccionó ningún elemento.");
                    return;
                }

                Element element = doc.GetElement(reference);
                DirectShape directShape = element as DirectShape;

                if (directShape == null)
                {
                    TaskDialog.Show("Error", "El elemento seleccionado no es un DirectShape.");
                    return;
                }

                // Extraer piezas
                var solids = ExtractSolids(directShape);

                if (solids.Count == 0)
                {
                    TaskDialog.Show("Sin Geometría",
                        "El DirectShape no contiene sólidos válidos para dividir.");
                    return;
                }

                if (solids.Count == 1)
                {
                    TaskDialog.Show("Una Sola Pieza",
                        $"El DirectShape solo contiene 1 sólido.\n\n" +
                        $"No hay necesidad de dividir.");
                    return;
                }

                // Confirmar con el usuario
                TaskDialogResult result = TaskDialog.Show(
                    "Dividir DirectShape",
                    $"Se encontraron {solids.Count} piezas individuales.\n\n" +
                    $"¿Desea crear {solids.Count} DirectShapes separados?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    TaskDialogResult.Yes);

                if (result != TaskDialogResult.Yes)
                    return;

                // Crear DirectShapes individuales
                int createdCount = 0;
                List<ElementId> newIds = new List<ElementId>();

                using (Transaction trans = new Transaction(doc, "Dividir DirectShape"))
                {
                    trans.Start();

                    try
                    {
                        // Obtener datos del DirectShape original
                        string originalName = directShape.Name;
                        ElementId categoryId = directShape.Category?.Id ?? new ElementId(BuiltInCategory.OST_GenericModel);
                        ElementId levelId = GetLevelId(doc, directShape);

                        // Crear un DirectShape por cada sólido
                        for (int i = 0; i < solids.Count; i++)
                        {
                            Solid solid = solids[i];

                            // Crear nuevo DirectShape
                            DirectShape newDirectShape = DirectShape.CreateElement(
                                doc,
                                categoryId);

                            // Asignar geometría
                            newDirectShape.SetShape(new List<GeometryObject> { solid });

                            // Asignar nombre
                            newDirectShape.Name = $"{originalName}_Pieza{i + 1}";

                            // Copiar parámetros relevantes
                            CopyParameters(directShape, newDirectShape);

                            newIds.Add(newDirectShape.Id);
                            createdCount++;
                        }

                        // Preguntar si eliminar el original
                        TaskDialogResult deleteOriginal = TaskDialog.Show(
                            "DirectShapes Creados",
                            $"Se crearon {createdCount} DirectShapes exitosamente.\n\n" +
                            $"¿Desea eliminar el DirectShape original?",
                            TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                            TaskDialogResult.No);

                        if (deleteOriginal == TaskDialogResult.Yes)
                        {
                            doc.Delete(directShape.Id);
                        }

                        trans.Commit();

                        // Seleccionar los nuevos elementos
                        uiDoc.Selection.SetElementIds(newIds);

                        TaskDialog.Show(
                            "Completado",
                            $"✅ Operación exitosa:\n\n" +
                            $"• {createdCount} DirectShapes creados\n" +
                            $"• Original {(deleteOriginal == TaskDialogResult.Yes ? "eliminado" : "conservado")}\n" +
                            $"• Nuevos elementos seleccionados");
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        TaskDialog.Show("Error en Transacción",
                            $"Error al crear DirectShapes:\n{ex.Message}");
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // Usuario canceló la selección
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error",
                    $"Error inesperado:\n{ex.Message}\n\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Extrae todos los sólidos de un DirectShape
        /// </summary>
        private List<Solid> ExtractSolids(DirectShape directShape)
        {
            List<Solid> solids = new List<Solid>();

            Options options = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            GeometryElement geomElement = directShape.get_Geometry(options);

            if (geomElement != null)
            {
                foreach (GeometryObject geomObj in geomElement)
                {
                    ExtractSolidsRecursive(geomObj, solids);
                }
            }

            // Filtrar sólidos válidos (con volumen > 0)
            return solids.Where(s => s != null && s.Volume > 0.001).ToList();
        }

        /// <summary>
        /// Extrae sólidos recursivamente (maneja GeometryInstance)
        /// </summary>
        private void ExtractSolidsRecursive(GeometryObject geomObj, List<Solid> solids)
        {
            if (geomObj is Solid solid)
            {
                if (solid.Volume > 0.001) // Ignorar sólidos vacíos
                {
                    solids.Add(solid);
                }
            }
            else if (geomObj is GeometryInstance geomInstance)
            {
                GeometryElement instanceGeometry = geomInstance.GetInstanceGeometry();
                if (instanceGeometry != null)
                {
                    foreach (GeometryObject obj in instanceGeometry)
                    {
                        ExtractSolidsRecursive(obj, solids);
                    }
                }
            }
            else if (geomObj is GeometryElement geomElement)
            {
                foreach (GeometryObject obj in geomElement)
                {
                    ExtractSolidsRecursive(obj, solids);
                }
            }
        }

        /// <summary>
        /// Obtiene el nivel asociado al DirectShape
        /// </summary>
        private ElementId GetLevelId(Document doc, DirectShape directShape)
        {
            // Intentar obtener el nivel del parámetro
            Parameter levelParam = directShape.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
            if (levelParam != null && levelParam.HasValue)
            {
                return levelParam.AsElementId();
            }

            // Si no tiene nivel, obtener el primer nivel del documento
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            Level firstLevel = collector.OfClass(typeof(Level)).FirstElement() as Level;
            return firstLevel?.Id ?? ElementId.InvalidElementId;
        }

        /// <summary>
        /// Copia parámetros relevantes del DirectShape original al nuevo
        /// </summary>
        private void CopyParameters(DirectShape original, DirectShape target)
        {
            try
            {
                // Copiar comentarios si existe
                Parameter commentsOriginal = original.LookupParameter("Comentarios");
                Parameter commentsTarget = target.LookupParameter("Comentarios");

                if (commentsOriginal != null && commentsTarget != null && commentsOriginal.HasValue)
                {
                    if (!commentsTarget.IsReadOnly)
                    {
                        commentsTarget.Set(commentsOriginal.AsString());
                    }
                }

                // Copiar marca si existe
                Parameter markOriginal = original.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                Parameter markTarget = target.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);

                if (markOriginal != null && markTarget != null && markOriginal.HasValue)
                {
                    if (!markTarget.IsReadOnly)
                    {
                        markTarget.Set(markOriginal.AsString());
                    }
                }
            }
            catch
            {
                // Ignorar errores al copiar parámetros
            }
        }
    }

    /// <summary>
    /// Filtro de selección para DirectShapes únicamente
    /// </summary>
    public class DirectShapeSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is DirectShape;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
