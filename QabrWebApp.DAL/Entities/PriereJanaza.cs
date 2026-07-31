using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QabrWebApp.Dal.Entities
{
    public enum StatutPriereEntity { AVenir, EnCours, Terminee, EnAttente, Brouillon }

    [Table("PrieresJanaza")]
    public class PriereJanaza
    {
        [Key]
        public int Id { get; set; }

        public int MosqueeId { get; set; }

        public int? UtilisateurId { get; set; }

        [MaxLength(200)]
        public string? NomDefunt { get; set; }

        public bool EstAnonyme { get; set; }

        public DateTime DateHeurePriere { get; set; }

        [MaxLength(10)]
        public string? Genre { get; set; }

        [MaxLength(1000)]
        public string? Commentaire { get; set; }

        [MaxLength(100)]
        public string? PaysEnterrement { get; set; }

        [MaxLength(200)]
        public string? VilleEnterrement { get; set; }

        public int? AnneeNaissance { get; set; }

        public int? AnneeDeces { get; set; }

        public int UtcOffsetMinutes { get; set; }

        public StatutPriereEntity Statut { get; set; }

        public DateTime DateCreation { get; set; }

        [ForeignKey(nameof(MosqueeId))]
        public Mosquee? Mosquee { get; set; }

        [ForeignKey(nameof(UtilisateurId))]
        public Utilisateur? Utilisateur { get; set; }
    }
}
