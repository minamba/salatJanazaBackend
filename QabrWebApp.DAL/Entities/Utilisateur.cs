using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QabrWebApp.Dal.Entities
{
    [Table("Utilisateurs")]
    public class Utilisateur
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(450)]
        public string IdentityUserId { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Prenom { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Nom { get; set; } = string.Empty;

        [Required, MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Telephone { get; set; }

        [MaxLength(500)]
        public string? ExpoToken { get; set; }

        [MaxLength(500)]
        public string? AdresseDomicile { get; set; }

        public double? LatitudeDomicile { get; set; }
        public double? LongitudeDomicile { get; set; }
        public int RayonNotification { get; set; } = 5;
        public bool NotifMouvement { get; set; }
        public DateTime DateInscription { get; set; }

        [MaxLength(50)]
        public string Role { get; set; } = "User";

        [MaxLength(10)]
        public string Language { get; set; } = "fr";

        public ICollection<Abonnement> Abonnements { get; set; } = [];
        public ICollection<PriereJanaza> PrieresDeclarees { get; set; } = [];
    }
}
