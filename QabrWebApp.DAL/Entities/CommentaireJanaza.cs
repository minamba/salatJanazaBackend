using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QabrWebApp.Dal.Entities
{
    [Table("CommentairesJanaza")]
    public class CommentaireJanaza
    {
        [Key]
        public int Id { get; set; }

        public int PriereJanazaId { get; set; }

        [MaxLength(100)]
        public string? AuteurNom { get; set; }

        [MaxLength(1000)]
        public string Contenu { get; set; } = "";

        public DateTime DateCreation { get; set; } = DateTime.UtcNow;

        public DateTime? DateModification { get; set; }

        public int? UtilisateurId { get; set; }

        public bool EstCache { get; set; } = false;

        public int? ParentCommentaireId { get; set; }

        [MaxLength(100)]
        public string? MentionNom { get; set; }

        [ForeignKey(nameof(PriereJanazaId))]
        public PriereJanaza? PriereJanaza { get; set; }
    }
}
