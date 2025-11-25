using ClosestGridsAddinVANILLA.Models;
using Newtonsoft.Json;
using System;
using System.IO;

namespace ClosestGridsAddinVANILLA.Services
{
    /// <summary>
    /// Gestiona el caché local de licencias para permitir uso offline
    /// </summary>
    public static class LicenseCacheManager
    {
        private static readonly string CacheFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClosestGridsAddin",
            "license.cache");

        private static readonly int GracePeriodDays = 7; // 7 días de gracia

        /// <summary>
        /// Guarda la licencia en caché local
        /// </summary>
        public static void SaveCache(LicenseInfo license)
        {
            try
            {
                // Crear directorio si no existe
                string directory = Path.GetDirectoryName(CacheFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Actualizar última verificación
                license.LastVerified = DateTime.UtcNow;

                // Serializar y guardar
                string json = JsonConvert.SerializeObject(license, Formatting.Indented);

                // Encriptar para seguridad básica
                string encrypted = EncryptString(json);
                File.WriteAllText(CacheFilePath, encrypted);

                System.Diagnostics.Debug.WriteLine($"License cache saved: {CacheFilePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving license cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Carga la licencia desde el caché local
        /// </summary>
        public static LicenseInfo LoadCache()
        {
            try
            {
                if (!File.Exists(CacheFilePath))
                    return null;

                // Leer y desencriptar
                string encrypted = File.ReadAllText(CacheFilePath);
                string json = DecryptString(encrypted);

                // Deserializar
                LicenseInfo license = JsonConvert.DeserializeObject<LicenseInfo>(json);

                System.Diagnostics.Debug.WriteLine($"License cache loaded: LastVerified={license.LastVerified}");
                return license;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading license cache: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Verifica si el caché es válido (dentro del grace period)
        /// </summary>
        public static bool IsCacheValid(LicenseInfo cache)
        {
            if (cache == null)
                return false;

            // Verificar que la licencia en sí sea válida
            if (!cache.IsValidNow())
                return false;

            // Verificar que no haya pasado el grace period
            TimeSpan timeSinceVerification = DateTime.UtcNow - cache.LastVerified;
            if (timeSinceVerification.TotalDays > GracePeriodDays)
                return false;

            return true;
        }

        /// <summary>
        /// Verifica si necesita re-verificar con Firebase (cada 24 horas)
        /// </summary>
        public static bool NeedsRevalidation(LicenseInfo cache)
        {
            if (cache == null)
                return true;

            TimeSpan timeSinceVerification = DateTime.UtcNow - cache.LastVerified;
            return timeSinceVerification.TotalHours > 24;
        }

        /// <summary>
        /// Limpia el caché (al cerrar sesión)
        /// </summary>
        public static void ClearCache()
        {
            try
            {
                if (File.Exists(CacheFilePath))
                {
                    File.Delete(CacheFilePath);
                    System.Diagnostics.Debug.WriteLine("License cache cleared");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Encriptación simple para el caché (no es seguridad militar, solo ofuscación)
        /// </summary>
        private static string EncryptString(string plainText)
        {
            try
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(plainText);
                string base64 = Convert.ToBase64String(data);

                // XOR simple con una clave
                byte[] key = System.Text.Encoding.UTF8.GetBytes("C10s3stGr1ds@2025");
                byte[] encrypted = Convert.FromBase64String(base64);

                for (int i = 0; i < encrypted.Length; i++)
                {
                    encrypted[i] = (byte)(encrypted[i] ^ key[i % key.Length]);
                }

                return Convert.ToBase64String(encrypted);
            }
            catch
            {
                return plainText; // Fallback sin encriptar
            }
        }

        /// <summary>
        /// Desencriptación simple
        /// </summary>
        private static string DecryptString(string encryptedText)
        {
            try
            {
                byte[] key = System.Text.Encoding.UTF8.GetBytes("C10s3stGr1ds@2025");
                byte[] encrypted = Convert.FromBase64String(encryptedText);

                for (int i = 0; i < encrypted.Length; i++)
                {
                    encrypted[i] = (byte)(encrypted[i] ^ key[i % key.Length]);
                }

                string base64 = Convert.ToBase64String(encrypted);
                byte[] data = Convert.FromBase64String(base64);
                return System.Text.Encoding.UTF8.GetString(data);
            }
            catch
            {
                return encryptedText; // Fallback sin desencriptar
            }
        }

        /// <summary>
        /// Obtiene información de estado del caché (para debugging)
        /// </summary>
        public static string GetCacheStatus()
        {
            var cache = LoadCache();
            if (cache == null)
                return "No cache";

            TimeSpan age = DateTime.UtcNow - cache.LastVerified;
            string status = IsCacheValid(cache) ? "VALID" : "EXPIRED";

            return $"{status} - Age: {age.TotalHours:F1}h - User: {cache.Email}";
        }
    }
}
