using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ClosestGridsAddinVANILLA.Models;

namespace ClosestGridsAddinVANILLA.Services
{
    /// <summary>
    /// Servicio para autenticación con Firebase Authentication usando REST API oficial
    /// </summary>
    public class FirebaseAuthenticationService
    {
        private readonly string _apiKey = "AIzaSyDHReu2GQRuUJTi4ygonBNzEhLL_6P9B5E";
        private readonly HttpClient _httpClient;

        public FirebaseAuthenticationService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// Autentica usuario con email y contraseña usando Firebase Authentication
        /// </summary>
        public async Task<AuthResult> SignInWithEmailAndPassword(string email, string password)
        {
            try
            {
                var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={_apiKey}";

                var requestBody = new
                {
                    email = email,
                    password = password,
                    returnSecureToken = true
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var error = JsonConvert.DeserializeObject<FirebaseErrorResponse>(responseBody);
                    return new AuthResult
                    {
                        Success = false,
                        ErrorMessage = GetFriendlyErrorMessage(error?.Error?.Message)
                    };
                }

                var authResponse = JsonConvert.DeserializeObject<FirebaseAuthResponse>(responseBody);

                return new AuthResult
                {
                    Success = true,
                    UserId = authResponse.LocalId,
                    Email = authResponse.Email,
                    IdToken = authResponse.IdToken,
                    RefreshToken = authResponse.RefreshToken,
                    DisplayName = authResponse.DisplayName
                };
            }
            catch (Exception ex)
            {
                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = $"Error de conexión: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Registra un nuevo usuario en Firebase Authentication
        /// </summary>
        public async Task<AuthResult> SignUpWithEmailAndPassword(string email, string password, string displayName = null)
        {
            try
            {
                var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={_apiKey}";

                var requestBody = new
                {
                    email = email,
                    password = password,
                    returnSecureToken = true
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var error = JsonConvert.DeserializeObject<FirebaseErrorResponse>(responseBody);
                    return new AuthResult
                    {
                        Success = false,
                        ErrorMessage = GetFriendlyErrorMessage(error?.Error?.Message)
                    };
                }

                var authResponse = JsonConvert.DeserializeObject<FirebaseAuthResponse>(responseBody);

                // Actualizar displayName si se proporcionó
                if (!string.IsNullOrEmpty(displayName))
                {
                    await UpdateProfile(authResponse.IdToken, displayName);
                }

                return new AuthResult
                {
                    Success = true,
                    UserId = authResponse.LocalId,
                    Email = authResponse.Email,
                    IdToken = authResponse.IdToken,
                    RefreshToken = authResponse.RefreshToken,
                    DisplayName = displayName
                };
            }
            catch (Exception ex)
            {
                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = $"Error de conexión: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Actualiza el perfil del usuario (displayName, photoUrl)
        /// </summary>
        private async Task UpdateProfile(string idToken, string displayName, string photoUrl = null)
        {
            try
            {
                var url = $"https://identitytoolkit.googleapis.com/v1/accounts:update?key={_apiKey}";

                var requestBody = new
                {
                    idToken = idToken,
                    displayName = displayName,
                    photoUrl = photoUrl ?? "",
                    returnSecureToken = true
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                await _httpClient.PostAsync(url, content);
            }
            catch
            {
                // Ignorar errores al actualizar perfil
            }
        }

        /// <summary>
        /// Renueva el token de autenticación usando refreshToken
        /// </summary>
        public async Task<AuthResult> RefreshToken(string refreshToken)
        {
            try
            {
                var url = $"https://securetoken.googleapis.com/v1/token?key={_apiKey}";

                var requestBody = new
                {
                    grant_type = "refresh_token",
                    refresh_token = refreshToken
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new AuthResult { Success = false, ErrorMessage = "No se pudo renovar la sesión" };
                }

                var refreshResponse = JsonConvert.DeserializeObject<FirebaseRefreshResponse>(responseBody);

                return new AuthResult
                {
                    Success = true,
                    UserId = refreshResponse.UserId,
                    IdToken = refreshResponse.IdToken,
                    RefreshToken = refreshResponse.RefreshToken
                };
            }
            catch (Exception ex)
            {
                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = $"Error al renovar sesión: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Envía email de recuperación de contraseña
        /// </summary>
        public async Task<bool> SendPasswordResetEmail(string email)
        {
            try
            {
                var url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={_apiKey}";

                var requestBody = new
                {
                    requestType = "PASSWORD_RESET",
                    email = email
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private string GetFriendlyErrorMessage(string firebaseError)
        {
            return firebaseError switch
            {
                "EMAIL_NOT_FOUND" => "No existe una cuenta con este correo electrónico",
                "INVALID_PASSWORD" => "Contraseña incorrecta",
                "USER_DISABLED" => "Esta cuenta ha sido deshabilitada",
                "EMAIL_EXISTS" => "Ya existe una cuenta con este correo electrónico",
                "OPERATION_NOT_ALLOWED" => "Operación no permitida. Contacte al administrador",
                "TOO_MANY_ATTEMPTS_TRY_LATER" => "Demasiados intentos fallidos. Intente más tarde",
                "INVALID_EMAIL" => "El formato del correo electrónico no es válido",
                "WEAK_PASSWORD" => "La contraseña debe tener al menos 6 caracteres",
                _ => $"Error de autenticación: {firebaseError}"
            };
        }
    }

    #region Response Models

    public class AuthResult
    {
        public bool Success { get; set; }
        public string UserId { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string IdToken { get; set; }
        public string RefreshToken { get; set; }
        public string ErrorMessage { get; set; }
    }

    internal class FirebaseAuthResponse
    {
        [JsonProperty("localId")]
        public string LocalId { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("idToken")]
        public string IdToken { get; set; }

        [JsonProperty("refreshToken")]
        public string RefreshToken { get; set; }

        [JsonProperty("expiresIn")]
        public string ExpiresIn { get; set; }
    }

    internal class FirebaseRefreshResponse
    {
        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("id_token")]
        public string IdToken { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("expires_in")]
        public string ExpiresIn { get; set; }
    }

    internal class FirebaseErrorResponse
    {
        [JsonProperty("error")]
        public FirebaseError Error { get; set; }
    }

    internal class FirebaseError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("errors")]
        public object[] Errors { get; set; }
    }

    #endregion
}
