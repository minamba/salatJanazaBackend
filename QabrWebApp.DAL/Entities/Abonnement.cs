using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QabrWebApp.Dal.Entities
{
    [Table("Abonnements")]
    public class Abonnement
    {
        [Key]
        public int Id { get; set; }

        public int UtilisateurId { get; set; }
        public int MosqueeId { get; set; }
        public bool NotifActive { get; set; }
        public DateTime DateAbonnement { get; set; }

        [ForeignKey(nameof(MosqueeId))]
        public Mosquee? Mosquee { get; set; }

        [ForeignKey(nameof(UtilisateurId))]
        public Utilisateur? Utilisateur { get; set; }
    }
}
