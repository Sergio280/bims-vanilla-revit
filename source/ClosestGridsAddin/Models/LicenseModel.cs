using System;
using Newtonsoft.Json;

namespace ClosestGridsAddinVANILLA.Models
{
    /// <summary>
    /// Modelo de licencia para el sistema de control de acceso
    /// Soporta tanto PascalCase como camelCase para compatibilidad con Firebase
    /// </summary>
    public class LicenseModel
    {
        [JsonProperty("licenseId")]
        public string LicenseId { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("userId")]
        public string UserId { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("licenseKey")]
        public string LicenseKey { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("expirationDate")]
        public DateTime ExpirationDate { get; set; }

        [JsonProperty("lastValidation")]
        public DateTime LastValidation { get; set; }

        [JsonProperty("isActive")]
        public bool IsActive { get; set; }

        [JsonProperty("licenseType")]
        public string LicenseType { get; set; } // "Trial", "Monthly", "Annual", "Lifetime"

        [JsonProperty("maxDevices")]
        public int MaxDevices { get; set; }

        /// <summary>
        /// Alias para MaxDevices (compatibilidad con nuevo sistema de activaciones)
        /// </summary>
        [JsonProperty("maxActivations")]
        public int MaxActivations
        {
            get => MaxDevices;
            set => MaxDevices = value;
        }

        [JsonProperty("machineId")]
        public string MachineId { get; set; }

        [JsonProperty("validationCount")]
        public int ValidationCount { get; set; }
    }
}
