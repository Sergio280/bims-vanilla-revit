using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitExtensions.Formwork;

namespace ClosestGridsAddinVANILLA.ENCOFRADO
{
    /// <summary>
    /// Comando para probar integración del patrón de dos pasos
    /// Este comando es temporal para validar la implementación
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class EncofradoPatronDosPasosCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Solicitar seleccion de elementos
                TaskDialog tdInicio = new TaskDialog("Encofrado - Patrón Dos Pasos")
                {
                    MainInstruction = "Patrón de Dos Pasos (NOTA)",
                    MainContent = "Los métodos del patrón de dos pasos ya están implementados\n" +
                                  "en la clase ComandoEncofradoUniversal.\n\n" +
                                  "Este comando es solo para validación.\n\n" +
                                  "RECOMENDACIÓN: Usar ComandoEncofradoUniversal directamente\n" +
                                  "y revisar el archivo de log para ver el patrón en acción.\n\n" +
                                  "Los métodos implementados son:\n" +
                                  "- CrearEncofradoPatronDospasos()\n" +
                                  "- CrearGeometriaVaciaConDescuentos()\n" +
                                  "- AjustarAlElementoEstructural()\n" +
                                  "- DetectarVigasIntersectadas()\n" +
                                  "- CrearMasaCorteColumnaCilindrica()\n\n" +
                                  "Puedes llamarlos desde otros comandos según necesites.",
                    CommonButtons = TaskDialogCommonButtons.Ok
                };

                tdInicio.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Error: {ex.Message}";
                TaskDialog.Show("Error", message);
                return Result.Failed;
            }
        }
    }
}
