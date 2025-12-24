using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.ENCOFRADO
{
    /// <summary>
    /// Comando de encofrado automatizado que usa el sistema integrado
    /// con reglas por tipo de elemento, dirección correcta, y conversión a Wall/Floor nativos
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class EncofradoAutomaticoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // PASO 1: Mostrar ventana de selección de tipos
                var dialog = new Views.EncofradoAutomaticoDialog(doc);
                bool? dialogResult = dialog.ShowDialog();

                if (dialogResult != true)
                {
                    return Result.Cancelled;
                }

                WallType wallType = dialog.WallTypeSeleccionado;
                FloorType floorType = dialog.FloorTypeSeleccionado;

                if (wallType == null || floorType == null)
                {
                    TaskDialog.Show("Error", "Debe seleccionar un tipo de muro y un tipo de suelo.");
                    return Result.Failed;
                }

                // PASO 2: Seleccionar elementos estructurales
                var selection = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new FiltroElementosEstructurales(),
                    "Seleccione los elementos estructurales a encofrar (columnas, vigas, muros, losas, escaleras)");

                if (selection.Count == 0)
                {
                    return Result.Cancelled;
                }

                // PASO 3: Procesar cada elemento seleccionado
                int totalMurosCreados = 0;
                int totalSuelosCreados = 0;
                int totalDirectShapes = 0;
                int totalElementosProcesados = 0;

                using (Transaction trans = new Transaction(doc, "Encofrado Automatizado"))
                {
                    trans.Start();

                    foreach (Reference reference in selection)
                    {
                        Element elemento = doc.GetElement(reference);
                        totalElementosProcesados++;

                        // Usar el sistema integrado
                        List<Element> encofrados = EncofradoIntegradoHelper.CrearEncofradoCompleto(
                            doc,
                            elemento,
                            wallType,
                            floorType);

                        // Contar resultados por tipo
                        foreach (var encofrado in encofrados)
                        {
                            if (encofrado is Wall)
                            {
                                totalMurosCreados++;
                            }
                            else if (encofrado is Floor)
                            {
                                totalSuelosCreados++;
                            }
                            else if (encofrado is DirectShape)
                            {
                                totalDirectShapes++;
                            }
                        }
                    }

                    trans.Commit();
                }

                // PASO 4: Mostrar resumen
                string resumen = $"✅ ENCOFRADO AUTOMATIZADO COMPLETADO\n\n" +
                               $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                               $"Elementos procesados: {totalElementosProcesados}\n\n" +
                               $"Encofrados creados:\n" +
                               $"  • Muros nativos: {totalMurosCreados}\n" +
                               $"  • Suelos nativos: {totalSuelosCreados}\n" +
                               $"  • DirectShapes: {totalDirectShapes}\n" +
                               $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                               $"Tipos usados:\n" +
                               $"  • Muros: {wallType.Name}\n" +
                               $"  • Suelos: {floorType.Name}\n\n" +
                               $"Sistema: Clasificación automática por tipo de elemento\n" +
                               $"  ✓ Columnas → caras verticales → Muros\n" +
                               $"  ✓ Vigas → laterales=Muros, inferior=Suelo\n" +
                               $"  ✓ Muros → laterales → Muros\n" +
                               $"  ✓ Losas → inferior → Suelo\n" +
                               $"  ✓ Escaleras → verticales=Muros, inclinadas=Suelos\n" +
                               $"  ✓ Dirección siempre hacia afuera\n" +
                               $"  ✓ Recortes automáticos por adyacentes";

                TaskDialog td = new TaskDialog("Encofrado Automatizado");
                td.MainInstruction = "Proceso Completado";
                td.MainContent = resumen;
                td.CommonButtons = TaskDialogCommonButtons.Ok;
                td.Show();

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"Error: {ex.Message}";
                TaskDialog.Show("Error", $"Error en encofrado automatizado:\n\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}");
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Filtro para selección de elementos estructurales
    /// </summary>
    public class FiltroElementosEstructurales : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem.Category == null) return false;

            var categoriaId = elem.Category.Id.Value;

            return categoriaId == (int)BuiltInCategory.OST_StructuralColumns ||
                   categoriaId == (int)BuiltInCategory.OST_StructuralFraming ||
                   categoriaId == (int)BuiltInCategory.OST_Walls ||
                   categoriaId == (int)BuiltInCategory.OST_Floors ||
                   categoriaId == (int)BuiltInCategory.OST_StructuralFoundation ||
                   categoriaId == (int)BuiltInCategory.OST_Stairs;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
