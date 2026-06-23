using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.Request
{
    public class PriereJanazaRequest
    {
        [Required]
        public int MosqueeId { get; set; }

        public int? UtilisateurId { get; set; }

        [MaxLength(200)]
        public string? NomDefunt { get; set; }

        public bool EstAnonyme { get; set; }

        [Required]
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
    }

    public class PriereJanazaUpdateStatutRequest
    {
        [Required]
        public string Statut { get; set; } = string.Empty;
    }
}
