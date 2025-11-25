using System;

namespace ClosestGridsAddinVANILLA.Models
{
    public class FirebaseModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Nombre { get; set; }
        public string Apellido { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int Status { get; set; }
        public string Role { get; set; }
    }
}
