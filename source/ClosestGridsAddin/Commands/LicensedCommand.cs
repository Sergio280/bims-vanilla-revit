using System;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosestGridsAddinVANILLA.Services;
using ClosestGridsAddinVANILLA.Views;

namespace ClosestGridsAddinVANILLA.Commands
{
    /// <summary>
    /// Clase base para todos los comandos que requieren validación de licencia.
    /// Maneja el flujo de autenticación y validación antes de ejecutar el comando específico.
    /// </summary>
    public abstract class LicensedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Validar licencia antes de ejecutar el comando
                if (!ValidateLicense(out string licenseMessage))
                {
                    message = licenseMessage;
                    TaskDialog.Show("Licencia no válida", licenseMessage);
                    return Result.Failed;
                }

                // Si la licencia es válida, ejecutar el comando específico
                return ExecuteCommand(commandData, ref message, elements);
            }
            catch (Exception ex)
            {
                message = $"Error al ejecutar el comando: {ex.Message}";
                TaskDialog.Show("Error", message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Método abstracto que deben implementar las clases derivadas con la lógica del comando.
        /// </summary>
        protected abstract Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements);

        /// <summary>
        /// Valida la licencia del usuario con sistema de activaciones por hardware.
        /// Usa caché offline con grace period de 7 días.
        /// Verifica contra Firebase cada 24 horas.
        /// </summary>
        private bool ValidateLicense(out string message)
        {
            message = string.Empty;

            try
            {
                // Obtener Hardware ID único de esta máquina
                string hardwareId = HardwareIdGenerator.GetHardwareId();
                System.Diagnostics.Debug.WriteLine($"Hardware ID: {hardwareId.Substring(0, 16)}...");

                // PASO 0: Verificar SessionCache (memoria RAM - válido durante sesión de Revit)
                if (SessionCache.HasValidSession())
                {
                    var session = SessionCache.GetSession();
                    System.Diagnostics.Debug.WriteLine($"✓ Sesión en memoria válida: {session.Email}");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No hay sesión en memoria, verificando caché de disco...");
                }

                // PASO 1: Verificar caché local primero (permite uso offline)
                var cachedLicense = LicenseCacheManager.LoadCache();

                if (cachedLicense != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Caché encontrado: {LicenseCacheManager.GetCacheStatus()}");

                    // Si el caché es válido (< 7 días) y la licencia es válida
                    if (LicenseCacheManager.IsCacheValid(cachedLicense) &&
                        cachedLicense.IsHardwareActivated(hardwareId))
                    {
                        // Verificar si necesita revalidación con Firebase (cada 24 horas)
                        if (!LicenseCacheManager.NeedsRevalidation(cachedLicense))
                        {
                            System.Diagnostics.Debug.WriteLine("✓ Usando caché offline válido (no requiere conexión)");

                            // Guardar en SessionCache para evitar verificaciones futuras en esta sesión
                            SessionCache.SetSession(new SessionData
                            {
                                UserId = cachedLicense.UserId,
                                Email = cachedLicense.Email,
                                DisplayName = cachedLicense.Email,
                                RefreshToken = "",
                                MachineId = hardwareId,
                                SavedAt = DateTime.Now
                            });
                            System.Diagnostics.Debug.WriteLine($"✓ Sesión cargada desde caché a memoria RAM: {cachedLicense.Email}");

                            return true;
                        }

                        // Necesita revalidar con Firebase (han pasado 24 horas)
                        System.Diagnostics.Debug.WriteLine("Revalidando con Firebase (24h transcurridas)...");
                        try
                        {
                            var activationService = new LicenseActivationService();
                            var verifyTask = Task.Run(async () =>
                                await activationService.VerifyActivationAsync(cachedLicense.UserId, hardwareId));

                            if (verifyTask.Wait(TimeSpan.FromSeconds(10)) && verifyTask.Result)
                            {
                                // Actualizar caché con datos frescos
                                var freshLicenseTask = Task.Run(async () =>
                                    await activationService.GetLicenseInfoAsync(cachedLicense.UserId));

                                if (freshLicenseTask.Wait(TimeSpan.FromSeconds(10)))
                                {
                                    var freshLicense = freshLicenseTask.Result;
                                    if (freshLicense != null && freshLicense.IsValidNow())
                                    {
                                        LicenseCacheManager.SaveCache(freshLicense);
                                        System.Diagnostics.Debug.WriteLine("✓ Licencia revalidada y caché actualizado");

                                        // Guardar en SessionCache
                                        SessionCache.SetSession(new SessionData
                                        {
                                            UserId = freshLicense.UserId,
                                            Email = freshLicense.Email,
                                            DisplayName = freshLicense.Email,
                                            RefreshToken = "",
                                            MachineId = hardwareId,
                                            SavedAt = DateTime.Now
                                        });
                                        System.Diagnostics.Debug.WriteLine($"✓ Sesión guardada en memoria RAM después de revalidación: {freshLicense.Email}");

                                        return true;
                                    }
                                }
                            }

                            // Firebase falló o timeout, pero caché aún válido (grace period)
                            System.Diagnostics.Debug.WriteLine("⚠ Firebase no respondió, usando grace period del caché");

                            // Guardar en SessionCache incluso con grace period
                            SessionCache.SetSession(new SessionData
                            {
                                UserId = cachedLicense.UserId,
                                Email = cachedLicense.Email,
                                DisplayName = cachedLicense.Email,
                                RefreshToken = "",
                                MachineId = hardwareId,
                                SavedAt = DateTime.Now
                            });

                            return true;
                        }
                        catch (Exception ex)
                        {
                            // Error de red, usar grace period del caché
                            System.Diagnostics.Debug.WriteLine($"⚠ Error de red: {ex.Message}, usando grace period");

                            // Guardar en SessionCache incluso con error de red
                            SessionCache.SetSession(new SessionData
                            {
                                UserId = cachedLicense.UserId,
                                Email = cachedLicense.Email,
                                DisplayName = cachedLicense.Email,
                                RefreshToken = "",
                                MachineId = hardwareId,
                                SavedAt = DateTime.Now
                            });

                            return true;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("⚠ Caché expirado o hardware no activado");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No hay caché local");
                }

                // PASO 2: No hay caché válido, necesita autenticación y activación
                var loginWindow = new LoginWindow();
                bool? dialogResult = loginWindow.ShowDialog();

                if (dialogResult != true || !loginWindow.LoginSuccessful)
                {
                    message = "Debe iniciar sesión para usar esta funcionalidad.";
                    return false;
                }

                string userId = loginWindow.UserId;
                System.Diagnostics.Debug.WriteLine($"Login exitoso: {loginWindow.UserEmail}");

                // PASO 3: Verificar y activar hardware en Firebase
                try
                {
                    var activationService = new LicenseActivationService();

                    // Intentar activar este hardware
                    System.Diagnostics.Debug.WriteLine("Activando hardware en Firebase...");
                    var activateTask = Task.Run(async () =>
                        await activationService.ActivateHardwareAsync(userId, hardwareId));

                    if (!activateTask.Wait(TimeSpan.FromSeconds(15)))
                    {
                        message = "Timeout al conectar con el servidor. Verifique su conexión a internet.";
                        return false;
                    }

                    // Obtener información completa de licencia
                    var licenseTask = Task.Run(async () =>
                        await activationService.GetLicenseInfoAsync(userId));

                    if (!licenseTask.Wait(TimeSpan.FromSeconds(10)))
                    {
                        message = "Timeout al obtener información de licencia.";
                        return false;
                    }

                    var license = licenseTask.Result;

                    if (license == null)
                    {
                        message = "No se pudo obtener información de licencia desde Firebase.";
                        return false;
                    }

                    if (!license.IsValidNow())
                    {
                        if (license.ExpirationDate.HasValue && license.ExpirationDate.Value < DateTime.UtcNow)
                        {
                            message = $"Su licencia expiró el {license.ExpirationDate.Value:dd/MM/yyyy}.\n" +
                                     "Renueve su suscripción para continuar usando el plugin.";
                        }
                        else if (!license.IsActive)
                        {
                            message = "Su licencia ha sido desactivada.\n" +
                                     "Contacte a soporte para más información.";
                        }
                        else
                        {
                            message = "Licencia no válida.";
                        }
                        return false;
                    }

                    // Guardar en caché de disco para uso offline
                    LicenseCacheManager.SaveCache(license);
                    System.Diagnostics.Debug.WriteLine($"✓ Licencia válida guardada en caché de disco ({license.Activations.Count}/{license.MaxActivations} activaciones)");

                    // Guardar en SessionCache (memoria RAM) para evitar pedir login en cada comando
                    var sessionData = new SessionData
                    {
                        UserId = userId,
                        Email = loginWindow.UserEmail,
                        DisplayName = loginWindow.UserDisplayName ?? loginWindow.UserEmail,
                        RefreshToken = loginWindow.RefreshToken ?? "",
                        MachineId = hardwareId,
                        SavedAt = DateTime.Now
                    };
                    SessionCache.SetSession(sessionData);
                    System.Diagnostics.Debug.WriteLine($"✓ Sesión guardada en memoria RAM: {sessionData.Email}");

                    return true;
                }
                catch (Exception ex)
                {
                    message = ex.Message;
                    System.Diagnostics.Debug.WriteLine($"✗ Error en activación: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                message = $"Error al validar la licencia: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"✗ Excepción en ValidateLicense: {ex}");
                return false;
            }
        }
    }
}
