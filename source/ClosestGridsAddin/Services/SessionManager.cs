using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace ClosestGridsAddinVANILLA.Services
{
    /// <summary>
    /// Gestiona sesiones de usuario de forma segura localmente
    /// </summary>
    public class SessionManager
    {
        private static readonly string SESSION_FILE_PATH = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClosestGridsAddin",
            "session.dat"
        );

        private static readonly string LOG_FILE_PATH = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClosestGridsAddin",
            "session_log.txt"
        );

        private static readonly byte[] KEY = Encoding.UTF8.GetBytes("BIMS2025RevitExtensions32Chars!"); // 32 bytes para AES-256
        private static readonly byte[] IV = Encoding.UTF8.GetBytes("BIMS2025Revit16!"); // 16 bytes para AES IV

        /// <summary>
        /// Escribe mensajes de log para debugging
        /// </summary>
        private static void LogMessage(string message)
        {
            try
            {
                var directory = Path.GetDirectoryName(LOG_FILE_PATH);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var logEntry = $"[{timestamp}] {message}{Environment.NewLine}";
                File.AppendAllText(LOG_FILE_PATH, logEntry);
            }
            catch
            {
                // Ignorar errores de logging
            }
        }

        /// <summary>
        /// Guarda la sesión del usuario cifrada
        /// </summary>
        public static bool SaveSession(SessionData session, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                // Crear directorio si no existe
                var directory = Path.GetDirectoryName(SESSION_FILE_PATH);
                if (!Directory.Exists(directory))
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                        LogMessage($"Directorio creado: {directory}");
                    }
                    catch (Exception ex)
                    {
                        errorMessage = $"Error al crear directorio: {ex.Message}";
                        LogMessage(errorMessage);
                        return false;
                    }
                }

                // Serializar sesión
                string json;
                try
                {
                    json = JsonConvert.SerializeObject(session);
                    LogMessage($"Sesión serializada: {json.Length} caracteres");
                }
                catch (Exception ex)
                {
                    errorMessage = $"Error al serializar sesión: {ex.Message}";
                    LogMessage(errorMessage);
                    return false;
                }

                // Cifrar datos
                byte[] encrypted;
                try
                {
                    encrypted = Encrypt(json);
                    LogMessage($"Datos cifrados: {encrypted.Length} bytes");
                }
                catch (Exception ex)
                {
                    errorMessage = $"Error al cifrar datos: {ex.Message}";
                    LogMessage(errorMessage);
                    return false;
                }

                // Guardar archivo
                try
                {
                    File.WriteAllBytes(SESSION_FILE_PATH, encrypted);
                    LogMessage($"Archivo guardado exitosamente: {SESSION_FILE_PATH}");
                    
                    // Verificar que el archivo existe y tiene contenido
                    if (File.Exists(SESSION_FILE_PATH))
                    {
                        var fileInfo = new FileInfo(SESSION_FILE_PATH);
                        LogMessage($"Archivo verificado: {fileInfo.Length} bytes");
                        return true;
                    }
                    else
                    {
                        errorMessage = "El archivo no se pudo verificar después de guardarlo";
                        LogMessage(errorMessage);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    errorMessage = $"Error al guardar archivo: {ex.Message}";
                    LogMessage(errorMessage);
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Error inesperado: {ex.Message}";
                LogMessage(errorMessage);
                return false;
            }
        }

        /// <summary>
        /// Sobrecarga para mantener compatibilidad
        /// </summary>
        public static bool SaveSession(SessionData session)
        {
            return SaveSession(session, out _);
        }

        /// <summary>
        /// Carga la sesión del usuario cifrada
        /// </summary>
        public static SessionData LoadSession(out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                if (!File.Exists(SESSION_FILE_PATH))
                {
                    errorMessage = "No existe archivo de sesión guardado";
                    LogMessage(errorMessage);
                    return null;
                }

                LogMessage($"Cargando sesión desde: {SESSION_FILE_PATH}");

                // Leer archivo cifrado
                byte[] encrypted;
                try
                {
                    encrypted = File.ReadAllBytes(SESSION_FILE_PATH);
                    LogMessage($"Archivo leído: {encrypted.Length} bytes");
                }
                catch (Exception ex)
                {
                    errorMessage = $"Error al leer archivo: {ex.Message}";
                    LogMessage(errorMessage);
                    ClearSession();
                    return null;
                }

                // Descifrar datos
                string json;
                try
                {
                    json = Decrypt(encrypted);
                    LogMessage($"Datos descifrados: {json.Length} caracteres");
                }
                catch (Exception ex)
                {
                    errorMessage = $"Error al descifrar datos: {ex.Message}";
                    LogMessage(errorMessage);
                    ClearSession();
                    return null;
                }

                // Deserializar sesión
                SessionData session;
                try
                {
                    session = JsonConvert.DeserializeObject<SessionData>(json);
                    LogMessage($"Sesión deserializada: UserId={session?.UserId ?? "null"}");
                }
                catch (Exception ex)
                {
                    errorMessage = $"Error al deserializar sesión: {ex.Message}";
                    LogMessage(errorMessage);
                    ClearSession();
                    return null;
                }

                // Validar que la sesión no haya expirado (7 días)
                if (DateTime.Now > session.SavedAt.AddDays(7))
                {
                    errorMessage = "La sesión ha expirado (más de 7 días)";
                    LogMessage(errorMessage);
                    ClearSession();
                    return null;
                }

                LogMessage("Sesión cargada exitosamente");
                return session;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error inesperado: {ex.Message}";
                LogMessage(errorMessage);
                // Si hay error al leer/descifrar, eliminar archivo corrupto
                ClearSession();
                return null;
            }
        }

        /// <summary>
        /// Sobrecarga para mantener compatibilidad
        /// </summary>
        public static SessionData LoadSession()
        {
            return LoadSession(out _);
        }

        /// <summary>
        /// Elimina la sesión guardada
        /// </summary>
        public static void ClearSession()
        {
            try
            {
                if (File.Exists(SESSION_FILE_PATH))
                {
                    File.Delete(SESSION_FILE_PATH);
                    LogMessage("Sesión eliminada");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error al eliminar sesión: {ex.Message}");
            }
        }

        /// <summary>
        /// Cifra datos usando AES
        /// </summary>
        private static byte[] Encrypt(string plainText)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = KEY;
                aes.IV = IV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            swEncrypt.Write(plainText);
                        }
                        return msEncrypt.ToArray();
                    }
                }
            }
        }

        /// <summary>
        /// Descifra datos usando AES
        /// </summary>
        private static string Decrypt(byte[] cipherText)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = KEY;
                aes.IV = IV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                        {
                            return srDecrypt.ReadToEnd();
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Datos de sesión del usuario
    /// </summary>
    public class SessionData
    {
        public string UserId { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string RefreshToken { get; set; }
        public string MachineId { get; set; }
        public DateTime SavedAt { get; set; }
    }
}
