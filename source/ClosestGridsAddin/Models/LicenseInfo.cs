using System;
using System.Collections.Generic;

namespace ClosestGridsAddinVANILLA.Models
{
    /// <summary>
    /// Información completa de licencia obtenida desde Firebase
    /// </summary>
    public class LicenseInfo
    {
        public string UserId { get; set; }
        public string Email { get; set; }
        public string LicenseType { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public int MaxActivations { get; set; }
        public Dictionary<string, ActivationInfo> Activations { get; set; }
        public bool IsActive { get; set; }
        public DateTime LastVerified { get; set; }

        public LicenseInfo()
        {
            Activations = new Dictionary<string, ActivationInfo>();
            IsActive = false;
            MaxActivations = 2; // Default: 2 máquinas
        }

        /// <summary>
        /// Verifica si la licencia es válida en este momento
        /// </summary>
        public bool IsValidNow()
        {
            // Verificar si está activa
            if (!IsActive)
                return false;

            // Verificar expiración
            if (ExpirationDate.HasValue && ExpirationDate.Value < DateTime.UtcNow)
                return false;

            return true;
        }

        /// <summary>
        /// Verifica si se puede activar en una nueva máquina
        /// </summary>
        public bool CanActivateNewMachine()
        {
            if (Activations == null)
                return true;

            return Activations.Count < MaxActivations;
        }

        /// <summary>
        /// Verifica si un hardware ID específico está activado
        /// </summary>
        public bool IsHardwareActivated(string hardwareId)
        {
            if (Activations == null)
                return false;

            return Activations.ContainsKey(hardwareId);
        }
    }

    /// <summary>
    /// Información de una activación individual
    /// </summary>
    public class ActivationInfo
    {
        public string HardwareId { get; set; }
        public DateTime ActivatedAt { get; set; }
        public DateTime LastSeen { get; set; }
        public string MachineName { get; set; }
        public string MachineInfo { get; set; }

        public ActivationInfo()
        {
            ActivatedAt = DateTime.UtcNow;
            LastSeen = DateTime.UtcNow;
        }
    }
}
