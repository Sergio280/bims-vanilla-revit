using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosestGridsAddinVANILLA.Services;
using ClosestGridsAddinVANILLA.Views;
using System;
using System.Threading.Tasks;

namespace ClosestGridsAddinVANILLA.Commands
{
    /// <summary>
    /// Comando para verificar el estado de la licencia del usuario.
    /// Hereda de LicensedCommand para validar automáticamente antes de ejecutar.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class LicenseTestCommand : LicensedCommand
    {
        protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Si llegamos aquí, la licencia ya fue validada por la clase base
                var machineId = LicenseService.GetMachineId();
                var session = SessionManager.LoadSession();

                if (session == null)
                {
                    message = "Error: No se pudo obtener la información de la sesión.";
                    return Result.Failed;
                }

                string sessionInfo = $"Usuario: {session.Email}\n" +
                                   $"ID: {session.UserId}\n" +
                                   $"Guardada: {session.SavedAt:dd/MM/yyyy HH:mm}\n" +
                                   $"MachineId Match: {session.MachineId == machineId}";

                string licenseInfo = "";
                var licenseService = new FirebaseLicenseService();
                var validationTask = Task.Run(async () =>
                    await licenseService.ValidateLicense(session.UserId, machineId)
                );

                if (validationTask.Wait(TimeSpan.FromSeconds(10)))
                {
                    var result = validationTask.Result;
                    licenseInfo = result.IsValid
                        ? $"\n\n✅ LICENCIA VÁLIDA\n{result.Message}\n\n" +
                          $"Tipo: {result.License.LicenseType}\n" +
                          $"Expira: {result.License.ExpirationDate:dd/MM/yyyy}\n" +
                          $"Validaciones: {result.License.ValidationCount}"
                        : $"\n\n❌ LICENCIA INVÁLIDA\n{result.Message}";
                }
                else
                {
                    licenseInfo = "\n\n⏱️ Timeout al validar licencia";
                }

                string fullMessage = $"=== INFORMACIÓN DE LICENCIA ===\n\n" +
                                   $"MachineId:\n{machineId}\n\n" +
                                   $"=== SESIÓN LOCAL ===\n{sessionInfo}" +
                                   $"{licenseInfo}";

                var dialog = new TaskDialog("Estado de Licencia")
                {
                    MainInstruction = "Información del Sistema de Licencias",
                    MainContent = fullMessage,
                    CommonButtons = TaskDialogCommonButtons.Close,
                    DefaultButton = TaskDialogResult.Close
                };

                // Agregar botones de acción
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Cerrar Sesión");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Abrir Login");

                var dialogResult = dialog.Show();

                if (dialogResult == TaskDialogResult.CommandLink1)
                {
                    SessionManager.ClearSession();
                    TaskDialog.Show("Sesión Cerrada", 
                        "La sesión local ha sido eliminada.\n" +
                        "Deberá iniciar sesión nuevamente al usar un comando.");
                }
                else if (dialogResult == TaskDialogResult.CommandLink2)
                {
                    var loginWindow = new LoginWindow();
                    loginWindow.ShowDialog();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Error al obtener información: {ex.Message}";
                TaskDialog.Show("Error", message);
                return Result.Failed;
            }
        }
    }
}
