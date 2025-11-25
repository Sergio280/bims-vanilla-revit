using System;
using System.Threading.Tasks;
using ClosestGridsAddinVANILLA.Models;
using FireSharp.Config;
using FireSharp.Interfaces;
using FireSharp.Response;
using Newtonsoft.Json;

namespace ClosestGridsAddinVANILLA.Services
{
    /// <summary>
    /// Servicio para gestión de licencias en Firebase Realtime Database
    /// </summary>
    public class FirebaseLicenseService
    {
        private readonly IFirebaseClient _client;

        private readonly IFirebaseConfig _config = new FirebaseConfig()
        {
            AuthSecret = "I2yypO4zT4LNHCG9NrBwI9VebMdOn9f4PiZwjlTY",
            BasePath = "https://bims-8d507-default-rtdb.firebaseio.com/"
        };

        public FirebaseLicenseService()
        {
            try
            {
                _client = new FireSharp.FirebaseClient(_config);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al conectar con Firebase Realtime Database: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifica si el cliente está conectado
        /// </summary>
        public bool IsConnected()
        {
            return _client != null;
        }

        /// <summary>
        /// Obtiene la licencia de un usuario por su UID de Firebase Authentication
        /// </summary>
        public async Task<LicenseModel> GetLicenseByUserId(string userId)
        {
            try
            {
                var response = await _client.GetAsync($"users/{userId}");

                if (response == null || response.Body == "null" || string.IsNullOrEmpty(response.Body))
                    return null;

                return JsonConvert.DeserializeObject<LicenseModel>(response.Body);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Crea o actualiza una licencia en Firebase
        /// </summary>
        public async Task<bool> SaveLicense(LicenseModel license)
        {
            try
            {
                license.LastValidation = DateTime.Now;
                var response = await _client.SetAsync($"users/{license.UserId}", license);
                return response != null && response.StatusCode == System.Net.HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Valida la licencia del usuario
        /// </summary>
        public async Task<LicenseValidationResult> ValidateLicense(string userId, string machineId)
        {
            try
            {
                var license = await GetLicenseByUserId(userId);

                if (license == null)
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Message = "No se encontró licencia para este usuario.\nContacte al administrador para obtener una licencia."
                    };
                }

                // Verificar si la licencia está activa
                if (!license.IsActive)
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Message = "Su licencia ha sido desactivada.\nContacte al administrador."
                    };
                }

                // Verificar fecha de expiración (comparar solo fechas, sin horas)
                var today = DateTime.Now.Date;
                var expirationDate = license.ExpirationDate.Date;

                // Licencia expira cuando today >= expirationDate (el día de expiración ya no tiene acceso)
                if (today >= expirationDate)
                {
                    // Desactivar licencia expirada
                    license.IsActive = false;
                    await SaveLicense(license);

                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Message = $"❌ Su licencia expiró el {license.ExpirationDate:dd/MM/yyyy}.\n\nPor favor, contacte al administrador para renovar su licencia."
                    };
                }

                // Verificar MachineId (si está configurado)
                if (string.IsNullOrEmpty(license.MachineId))
                {
                    // Primera validación: asignar máquina
                    license.MachineId = machineId;
                    license.ValidationCount = 1;
                    license.LastValidation = DateTime.Now;
                    await SaveLicense(license);
                }
                else if (license.MachineId != machineId)
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Message = "Esta licencia está vinculada a otro equipo.\nContacte al administrador para transferir la licencia."
                    };
                }
                else
                {
                    // Actualizar contador de validaciones
                    license.ValidationCount++;
                    license.LastValidation = DateTime.Now;
                    await SaveLicense(license);
                }

                // Licencia válida - calcular días restantes
                int daysRemaining = (expirationDate - today).Days;

                string warningMessage = "";
                if (daysRemaining == 1)
                {
                    warningMessage = "\n\n⚠️ ÚLTIMO DÍA: Su licencia expira mañana.";
                }
                else if (daysRemaining <= 7)
                {
                    warningMessage = $"\n\n⚠️ ADVERTENCIA: Su licencia expira en {daysRemaining} días.";
                }

                return new LicenseValidationResult
                {
                    IsValid = true,
                    License = license,
                    Message = $"✅ Licencia válida hasta {license.ExpirationDate:dd/MM/yyyy}\n({daysRemaining} día{(daysRemaining != 1 ? "s" : "")} restante{(daysRemaining != 1 ? "s" : "")}){warningMessage}"
                };
            }
            catch (Exception ex)
            {
                return new LicenseValidationResult
                {
                    IsValid = false,
                    Message = $"Error al validar licencia: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Crea una nueva licencia para un usuario
        /// </summary>
        public async Task<bool> CreateTrialLicense(string userId, string email, string machineId)
        {
            try
            {
                var license = new LicenseModel
                {
                    UserId = userId,
                    Email = email,
                    LicenseType = "Trial",
                    CreatedAt = DateTime.Now,
                    ExpirationDate = DateTime.Now.AddDays(30), // 30 días de prueba
                    IsActive = true,
                    MaxDevices = 1,
                    MachineId = machineId,
                    ValidationCount = 0,
                    LastValidation = DateTime.Now,
                    LicenseKey = LicenseService.GenerateLicenseKey(email, DateTime.Now.AddDays(30))
                };

                return await SaveLicense(license);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Actualiza la última validación de la licencia
        /// </summary>
        public async Task UpdateLastValidation(string userId)
        {
            try
            {
                var license = await GetLicenseByUserId(userId);
                if (license != null)
                {
                    license.LastValidation = DateTime.Now;
                    license.ValidationCount++;
                    await SaveLicense(license);
                }
            }
            catch
            {
                // Ignorar errores al actualizar validación
            }
        }
    }

    /// <summary>
    /// Resultado de la validación de licencia
    /// </summary>
    public class LicenseValidationResult
    {
        public bool IsValid { get; set; }
        public LicenseModel License { get; set; }
        public string Message { get; set; }
    }
}
