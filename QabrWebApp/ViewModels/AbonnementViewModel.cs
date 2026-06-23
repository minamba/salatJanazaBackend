namespace QabrWebApp.ViewModels
{
    public class AbonnementViewModel
    {
        public int Id { get; set; }
        public int UtilisateurId { get; set; }
        public int MosqueeId { get; set; }
        public string? MosqueeNom { get; set; }
        public string? MosqueeAdresse { get; set; }
        public double? MosqueeLatitude { get; set; }
        public double? MosqueeLongitude { get; set; }
        public string? MosqueeOsmId { get; set; }
        public bool NotifActive { get; set; }
        public DateTime DateAbonnement { get; set; }
    }
}
