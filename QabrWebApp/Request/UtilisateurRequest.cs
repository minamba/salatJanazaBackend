using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.Request
{
    public class UtilisateurRequest
    {
        [Required]
        public string IdentityUserId { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Prenom { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Nom { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Telephone { get; set; }

        [MaxLength(10)]
        public string Language { get; set; } = "fr";
    }

    public class UtilisateurUpdateRequest
    {
        [MaxLength(100)]
        public string? Prenom { get; set; }

        [MaxLength(100)]
        public string? Nom { get; set; }

        [MaxLength(20)]
        public string? Telephone { get; set; }

        [MaxLength(500)]
        public string? ExpoToken { get; set; }

        [MaxLength(500)]
        public string? AdresseDomicile { get; set; }

        public double? LatitudeDomicile { get; set; }
        public double? LongitudeDomicile { get; set; }

        [Range(1, 40000)]
        public int? RayonNotification { get; set; }

        public bool? NotifMouvement { get; set; }

        [MaxLength(10)]
        public string? Language { get; set; }

        public double? LatitudeCourante { get; set; }
        public double? LongitudeCourante { get; set; }

        [MaxLength(10)]
        public string? ModeLocalisation { get; set; }

        [MaxLength(10)]
        public string? Platform { get; set; }
    }

    public class AdminUpdateRoleRequest
    {
        [Required]
        public string Role { get; set; } = "User";
    }
}
