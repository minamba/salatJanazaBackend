namespace QabrWebApp.ViewModels
{
    public class MosqueeViewModel
    {
        public int Id { get; set; }
        public string Nom { get; set; } = string.Empty;
        public string? Adresse { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? OsmId { get; set; }
        public string Statut { get; set; } = "Validee";
        public string Source { get; set; } = "user";
        public DateTime DateCreation { get; set; }
        public double? DistanceKm { get; set; }
    }
}
