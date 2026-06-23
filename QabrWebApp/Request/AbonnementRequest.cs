using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.Request
{
    public class AbonnementRequest
    {
        [Required]
        public int UtilisateurId { get; set; }

        [Required]
        public int MosqueeId { get; set; }
    }

    public class AbonnementToggleRequest
    {
        [Required]
        public bool NotifActive { get; set; }
    }
}
