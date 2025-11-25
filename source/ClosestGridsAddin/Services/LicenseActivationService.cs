using ClosestGridsAddinVANILLA.Models;
using FireSharp.Config;
using FireSharp.Interfaces;
using FireSharp.Response;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ClosestGridsAddinVANILLA.Services
{
    /// <summary>
    /// Servicio para gestionar activaciones de licencias en Firebase
    /// </summary>
    public class LicenseActivationService
    {
        private readonly IFirebaseClient _client;

        public LicenseActivationService()
        {
            IFirebaseConfig config = new FirebaseConfig
            {
                AuthSecret = "I2yypO4zT4LNHCG9NrBwI9VebMdOn9f4PiZwjlTY",
                BasePath = "https://bims-8d507-default-rtdb.firebaseio.com/"
            };

            _client = new FireSharp.FirebaseClient(config);
        }

        /// <summary>
        /// Obtiene información completa de licencia desde Firebase
        /// </summary>
        public async Task<LicenseInfo> GetLicenseInfoAsync(string userId)
        {
            try
            {
                FirebaseResponse response = await _client.GetAsync($"users/{userId}");

                if (response.Body == "null" || string.IsNullOrEmpty(response.Body))
                {
                    return null;
                }

                var userData = response.ResultAs<Dictionary<string, object>>();

                var license = new LicenseInfo
                {
                    UserId = userId,
                    Email = userData.ContainsKey("email") ? userData["email"]?.ToString() : "",
                    LicenseType = userData.ContainsKey("licenseType") ? userData["licenseType"]?.ToString() : "free",
                    IsActive = userData.ContainsKey("isActive") ? Convert.ToBoolean(userData["isActive"]) : true,
                    MaxActivations = userData.ContainsKey("maxActivations") ? Convert.ToInt32(userData["maxActivations"]) : 2
                };

                // Parsear fecha de expiración
                if (userData.ContainsKey("expirationDate"))
                {
                    string expDateStr = userData["expirationDate"]?.ToString();
                    if (!string.IsNullOrEmpty(expDateStr) && DateTime.TryParse(expDateStr, out DateTime expDate))
                    {
                        license.ExpirationDate = expDate;
                    }
                }

                // Obtener activaciones
                if (userData.ContainsKey("activations"))
                {
                    var activationsData = userData["activations"] as Dictionary<string, object>;
                    if (activationsData != null)
                    {
                        foreach (var kvp in activationsData)
                        {
                            var actData = kvp.Value as Dictionary<string, object>;
                            if (actData != null)
                            {
                                var activation = new ActivationInfo
                                {
                                    HardwareId = kvp.Key,
                                    MachineName = actData.ContainsKey("machineName") ? actData["machineName"]?.ToString() : "",
                                    MachineInfo = actData.ContainsKey("machineInfo") ? actData["machineInfo"]?.ToString() : ""
                                };

                                if (actData.ContainsKey("activatedAt"))
                                {
                                    DateTime.TryParse(actData["activatedAt"]?.ToString(), out DateTime actDate);
                                    activation.ActivatedAt = actDate;
                                }

                                if (actData.ContainsKey("lastSeen"))
                                {
                                    DateTime.TryParse(actData["lastSeen"]?.ToString(), out DateTime lastSeen);
                                    activation.LastSeen = lastSeen;
                                }

                                license.Activations[kvp.Key] = activation;
                            }
                        }
                    }
                }

                return license;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting license info: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Registra una nueva activación de hardware
        /// </summary>
        public async Task<bool> ActivateHardwareAsync(string userId, string hardwareId)
        {
            try
            {
                // Primero verificar que no se haya excedido el límite
                var license = await GetLicenseInfoAsync(userId);

                if (license == null)
                {
                    throw new Exception("Licencia no encontrada");
                }

                if (!license.IsValidNow())
                {
                    throw new Exception("Licencia no válida o expirada");
                }

                // Verificar si ya está activado
                if (license.IsHardwareActivated(hardwareId))
                {
                    // Ya activado, solo actualizar lastSeen
                    await UpdateLastSeenAsync(userId, hardwareId);
                    return true;
                }

                // Verificar límite de activaciones
                if (!license.CanActivateNewMachine())
                {
                    throw new Exception($"Límite de activaciones alcanzado ({license.MaxActivations} máquinas). Desactiva una máquina desde el panel web.");
                }

                // Crear nueva activación
                var activation = new ActivationInfo
                {
                    HardwareId = hardwareId,
                    ActivatedAt = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow,
                    MachineName = Environment.MachineName,
                    MachineInfo = HardwareIdGenerator.GetMachineInfo()
                };

                var activationData = new Dictionary<string, object>
                {
                    { "activatedAt", activation.ActivatedAt.ToString("o") },
                    { "lastSeen", activation.LastSeen.ToString("o") },
                    { "machineName", activation.MachineName },
                    { "machineInfo", activation.MachineInfo }
                };

                // Guardar en Firebase
                await _client.SetAsync($"users/{userId}/activations/{hardwareId}", activationData);

                System.Diagnostics.Debug.WriteLine($"Hardware {hardwareId} activated for user {userId}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error activating hardware: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Actualiza el timestamp de última conexión
        /// </summary>
        public async Task UpdateLastSeenAsync(string userId, string hardwareId)
        {
            try
            {
                await _client.SetAsync($"users/{userId}/activations/{hardwareId}/lastSeen",
                    DateTime.UtcNow.ToString("o"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating last seen: {ex.Message}");
                // No lanzar excepción, no es crítico
            }
        }

        /// <summary>
        /// Desactiva un hardware (libera un slot de activación)
        /// </summary>
        public async Task<bool> DeactivateHardwareAsync(string userId, string hardwareId)
        {
            try
            {
                await _client.DeleteAsync($"users/{userId}/activations/{hardwareId}");
                System.Diagnostics.Debug.WriteLine($"Hardware {hardwareId} deactivated for user {userId}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deactivating hardware: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Verifica que el hardware esté activado y la licencia sea válida
        /// </summary>
        public async Task<bool> VerifyActivationAsync(string userId, string hardwareId)
        {
            try
            {
                var license = await GetLicenseInfoAsync(userId);

                if (license == null)
                    return false;

                if (!license.IsValidNow())
                    return false;

                if (!license.IsHardwareActivated(hardwareId))
                    return false;

                // Actualizar lastSeen
                await UpdateLastSeenAsync(userId, hardwareId);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error verifying activation: {ex.Message}");
                return false;
            }
        }
    }
}
