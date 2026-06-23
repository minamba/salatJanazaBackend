namespace QabrWebApp.ViewModels
{
    public class PriereJanazaViewModel
    {
        public int Id { get; set; }
        public int MosqueeId { get; set; }
        public string? MosqueeNom { get; set; }
        public string? MosqueeAdresse { get; set; }
        public int? UtilisateurId { get; set; }
        public string? NomDefunt { get; set; }
        public bool EstAnonyme { get; set; }
        public DateTime DateHeurePriere { get; set; }
        public string? Genre { get; set; }
        public string? Commentaire { get; set; }
        public string? PaysEnterrement { get; set; }
        public string? VilleEnterrement { get; set; }
        public int? AnneeNaissance { get; set; }
        public int? AnneeDeces { get; set; }
        public int UtcOffsetMinutes { get; set; }
        public string Statut { get; set; } = string.Empty;
        public DateTime DateCreation { get; set; }
        public double? MosqueeLatitude { get; set; }
        public double? MosqueeLongitude { get; set; }
    }
}
