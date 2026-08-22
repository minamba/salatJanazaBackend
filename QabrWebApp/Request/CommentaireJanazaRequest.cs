using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.Request
{
    public class CommentaireJanazaRequest
    {
        [MaxLength(100)]
        public string? AuteurNom { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Contenu { get; set; } = "";

        public int? ParentCommentaireId { get; set; }

        [MaxLength(100)]
        public string? MentionNom { get; set; }

        public int? UtilisateurId { get; set; }
    }
}
