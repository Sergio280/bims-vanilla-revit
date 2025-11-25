using System;
using System.Security.Cryptography;
using System.Text;
using System.Management;
using System.Linq;

namespace ClosestGridsAddinVANILLA.Services
{
    /// <summary>
    /// Servicio para gestión de licencias locales
    /// </summary>
    public class LicenseService
    {
        private static readonly string SALT = "RevitExtensions_SALT_2025";

        /// <summary>
        /// Obtiene un identificador único de la máquina actual
        /// </summary>
        public static string GetMachineId()
        {
            try
            {
                string processorId = GetProcessorId();
                string motherboardId = GetMotherboardId();
                string combined = $"{processorId}-{motherboardId}";
                
                return GenerateHash(combined);
            }
            catch
            {
                // Fallback a un identificador basado en el nombre de la máquina
                return GenerateHash(Environment.MachineName);
            }
        }

        /// <summary>
        /// Genera una clave de licencia basada en el email y fecha de expiración
        /// </summary>
        public static string GenerateLicenseKey(string email, DateTime expirationDate)
        {
            string data = $"{email}|{expirationDate:yyyyMMdd}|{SALT}";
            return GenerateHash(data).Substring(0, 25).ToUpper();
        }

        /// <summary>
        /// Valida si una licencia es válida
        /// </summary>
        public static bool ValidateLicense(string email, string licenseKey, DateTime expirationDate)
        {
            string expectedKey = GenerateLicenseKey(email, expirationDate);
            return licenseKey == expectedKey && DateTime.Now <= expirationDate;
        }

        private static string GetProcessorId()
        {
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                ManagementObjectCollection collection = searcher.Get();
                string processorId = string.Empty;

                foreach (ManagementObject obj in collection)
                {
                    processorId = obj["ProcessorId"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(processorId))
                        break;
                }

                return processorId;
            }
            catch
            {
                return "UNKNOWN_PROCESSOR";
            }
        }

        private static string GetMotherboardId()
        {
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                ManagementObjectCollection collection = searcher.Get();
                string serialNumber = string.Empty;

                foreach (ManagementObject obj in collection)
                {
                    serialNumber = obj["SerialNumber"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(serialNumber))
                        break;
                }

                return serialNumber;
            }
            catch
            {
                return "UNKNOWN_MOTHERBOARD";
            }
        }

        private static string GenerateHash(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder builder = new StringBuilder();
                
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                
                return builder.ToString();
            }
        }
    }
}
