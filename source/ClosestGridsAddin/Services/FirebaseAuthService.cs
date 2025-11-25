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
    /// Servicio para autenticación y gestión de licencias con Firebase
    /// </summary>
    public class FirebaseAuthService
    {
        private readonly IFirebaseClient _client;
        
        private readonly IFirebaseConfig _config = new FirebaseConfig()
        {
            AuthSecret = "I2yypO4zT4LNHCG9NrBwI9VebMdOn9f4PiZwjlTY",
            BasePath = "https://bims-8d507-default-rtdb.firebaseio.com/"
        };

        public FirebaseAuthService()
        {
            try
            {
                _client = new FireSharp.FirebaseClient(_config);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al conectar con Firebase: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifica si el cliente Firebase está conectado
        /// </summary>
        public bool IsConnected()
        {
            return _client != null;
        }

        /// <summary>
        /// Registra un nuevo usuario en Firebase
        /// </summary>
        public async Task<bool> RegisterUser(string email, string password, string displayName)
        {
            try
            {
                // Crear modelo de usuario
                var user = new UserModel
                {
                    Email = email,
                    DisplayName = displayName,
                    CreatedAt = DateTime.Now,
                    LastLogin = DateTime.Now,
                    IsActive = true,
                    Role = "User"
                };

                // Guardar en Firebase
                var response = await _client.SetAsync($"Users/{user.UserId}", user);
                
                // Guardar credenciales (en producción usar hash seguro)
                var credentials = new
                {
                    Email = email,
                    PasswordHash = HashPassword(password),
                    UserId = user.UserId
                };
                
                await _client.SetAsync($"Credentials/{email.Replace(".", "_")}", credentials);
                
                return response.StatusCode == System.Net.HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Autentica un usuario con email y contraseña
        /// </summary>
        public async Task<UserModel> AuthenticateUser(string email, string password)
        {
            try
            {
                // Obtener credenciales
                var credResponse = await _client.GetAsync($"Credentials/{email.Replace(".", "_")}");
                
                if (credResponse.Body == "null")
                    return null;

                var credentials = JsonConvert.DeserializeObject<dynamic>(credResponse.Body);
                
                // Verificar contraseña
                string storedHash = credentials.PasswordHash.ToString();
                string inputHash = HashPassword(password);
                
                if (storedHash != inputHash)
                    return null;

                // Obtener usuario
                string userId = credentials.UserId.ToString();
                var userResponse = await _client.GetAsync($"Users/{userId}");
                
                if (userResponse.Body == "null")
                    return null;

                var user = JsonConvert.DeserializeObject<UserModel>(userResponse.Body);
                
                // Actualizar último login
                user.LastLogin = DateTime.Now;
                await _client.UpdateAsync($"Users/{userId}", user);
                
                return user;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Obtiene la licencia de un usuario
        /// </summary>
        public async Task<LicenseModel> GetUserLicense(string userId)
        {
            try
            {
                var response = await _client.GetAsync($"Licenses/{userId}");
                
                if (response.Body == "null")
                    return null;

                return JsonConvert.DeserializeObject<LicenseModel>(response.Body);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Crea o actualiza la licencia de un usuario
        /// </summary>
        public async Task<bool> CreateOrUpdateLicense(LicenseModel license)
        {
            try
            {
                license.LastValidation = DateTime.Now;
                var response = await _client.SetAsync($"Licenses/{license.UserId}", license);
                return response.StatusCode == System.Net.HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Valida la licencia de un usuario
        /// </summary>
        public async Task<bool> ValidateLicense(string userId, string machineId)
        {
            try
            {
                var license = await GetUserLicense(userId);
                
                if (license == null)
                    return false;

                // Verificar estado activo
                if (!license.IsActive)
                    return false;

                // Verificar fecha de expiración
                if (DateTime.Now > license.ExpirationDate)
                {
                    license.IsActive = false;
                    await CreateOrUpdateLicense(license);
                    return false;
                }

                // Verificar máquina (si está configurado)
                if (!string.IsNullOrEmpty(license.MachineId) && license.MachineId != machineId)
                    return false;

                // Actualizar contador de validaciones
                license.ValidationCount++;
                license.LastValidation = DateTime.Now;
                await CreateOrUpdateLicense(license);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Genera hash simple de contraseña (usar bcrypt en producción)
        /// </summary>
        private string HashPassword(string password)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(password + "SALT_2025");
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }
    }
}
