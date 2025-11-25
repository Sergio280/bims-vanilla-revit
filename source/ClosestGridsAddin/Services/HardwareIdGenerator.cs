using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace ClosestGridsAddinVANILLA.Services
{
    /// <summary>
    /// Genera un ID único de hardware basado en componentes de la máquina
    /// Este ID es estable y único por máquina para prevenir compartir licencias
    /// </summary>
    public static class HardwareIdGenerator
    {
        /// <summary>
        /// Genera el Hardware ID de la máquina actual
        /// Combina: CPU ID + Motherboard Serial + MAC Address primera NIC
        /// </summary>
        public static string GetHardwareId()
        {
            try
            {
                string cpuId = GetCpuId();
                string motherboardSerial = GetMotherboardSerial();
                string macAddress = GetMacAddress();

                // Combinar los componentes
                string combined = $"{cpuId}|{motherboardSerial}|{macAddress}";

                // Generar hash SHA256 para tener ID de longitud fija
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in hashBytes)
                    {
                        sb.Append(b.ToString("x2"));
                    }
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                // Fallback: usar nombre de máquina + usuario
                // No es ideal pero permite funcionar si WMI falla
                System.Diagnostics.Debug.WriteLine($"Error generando Hardware ID: {ex.Message}");
                return GetFallbackHardwareId();
            }
        }

        /// <summary>
        /// Obtiene el ID del procesador
        /// </summary>
        private static string GetCpuId()
        {
            try
            {
                string cpuInfo = "";
                ManagementClass mc = new ManagementClass("Win32_Processor");
                ManagementObjectCollection moc = mc.GetInstances();

                foreach (ManagementObject mo in moc)
                {
                    cpuInfo = mo.Properties["ProcessorId"].Value?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(cpuInfo))
                        break;
                }
                moc.Dispose();
                mc.Dispose();

                return cpuInfo;
            }
            catch
            {
                return Environment.ProcessorCount.ToString();
            }
        }

        /// <summary>
        /// Obtiene el serial number de la motherboard
        /// </summary>
        private static string GetMotherboardSerial()
        {
            try
            {
                string serial = "";
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");

                foreach (ManagementObject mo in searcher.Get())
                {
                    serial = mo["SerialNumber"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(serial))
                        break;
                }
                searcher.Dispose();

                return serial;
            }
            catch
            {
                return Environment.MachineName;
            }
        }

        /// <summary>
        /// Obtiene la MAC Address de la primera NIC activa
        /// </summary>
        private static string GetMacAddress()
        {
            try
            {
                ManagementClass mc = new ManagementClass("Win32_NetworkAdapterConfiguration");
                ManagementObjectCollection moc = mc.GetInstances();

                foreach (ManagementObject mo in moc)
                {
                    if ((bool)mo["IPEnabled"] == true)
                    {
                        string mac = mo["MacAddress"]?.ToString();
                        if (!string.IsNullOrEmpty(mac))
                        {
                            moc.Dispose();
                            mc.Dispose();
                            return mac;
                        }
                    }
                }
                moc.Dispose();
                mc.Dispose();

                return "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// ID de fallback si WMI no está disponible
        /// </summary>
        private static string GetFallbackHardwareId()
        {
            string fallback = $"{Environment.MachineName}|{Environment.UserName}|{Environment.ProcessorCount}";

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(fallback));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Obtiene información legible sobre la máquina (para mostrar al usuario)
        /// </summary>
        public static string GetMachineInfo()
        {
            try
            {
                return $"{Environment.MachineName} ({Environment.UserName})";
            }
            catch
            {
                return "Máquina desconocida";
            }
        }
    }
}
