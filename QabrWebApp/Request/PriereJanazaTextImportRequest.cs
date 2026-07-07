using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.Request
{
    public class PriereJanazaTextImportRequest
    {
        /// <summary>Token GUID du fichier .txt uploadé — sert uniquement de référence/groupement, aucune session requise.</summary>
        [Required]
        public string TextImportToken { get; set; } = string.Empty;

        public int? UtilisateurId { get; set; }

        [Required, MaxLength(300)]
        public string MosqueeNom { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? MosqueeAdresse { get; set; }

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
    }
}
