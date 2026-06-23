using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QabrWebApp.Dal.Entities
{
    [Table("Mosquees")]
    public class Mosquee
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Nom { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Adresse { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        [MaxLength(50)]
        public string? OsmId { get; set; }

        [MaxLength(20)]
        public string Statut { get; set; } = "Validee";

        [MaxLength(10)]
        public string Source { get; set; } = "user"; // "user" | "osm"

        public DateTime DateCreation { get; set; }

        public DateTime? DerniereSyncOsm { get; set; }

        public ICollection<PriereJanaza> PrieresJanaza { get; set; } = [];
        public ICollection<Abonnement> Abonnements { get; set; } = [];
    }
}
