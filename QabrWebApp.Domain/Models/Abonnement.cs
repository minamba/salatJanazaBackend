namespace QabrWebApp.Domain.Models
{
    public class Abonnement
    {
        public int Id { get; set; }
        public int UtilisateurId { get; set; }
        public int MosqueeId { get; set; }
        public bool NotifActive { get; set; }
        public DateTime DateAbonnement { get; set; }
        public Mosquee? Mosquee { get; set; }
        public Utilisateur? Utilisateur { get; set; }
    }
}
