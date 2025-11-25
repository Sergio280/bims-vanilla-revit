using System;

namespace ClosestGridsAddinVANILLA.Models
{
    /// <summary>
    /// Modelo de usuario para autenticación con Firebase
    /// </summary>
    public class UserModel
    {
        public string UserId { get; set; } = Guid.NewGuid().ToString();
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastLogin { get; set; }
        public bool IsActive { get; set; }
        public string Role { get; set; }
    }
}
