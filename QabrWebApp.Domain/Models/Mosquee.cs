namespace QabrWebApp.Domain.Models
{
    public class Mosquee
    {
        public int Id { get; set; }
        public string Nom { get; set; } = string.Empty;
        public string? Adresse { get; set; }

        /// <summary>Ville issue du géocodage inverse des coordonnées. Null si introuvable.</summary>
        public string? Ville { get; set; }

        /// <summary>Pays en clair, issu du même géocodage. Null si introuvable.</summary>
        public string? Pays { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? OsmId { get; set; }
        public string Statut { get; set; } = "Validee"; // "Validee" | "EnAttente"
        public string Source { get; set; } = "user"; // "user" | "osm"
        public int? UtilisateurId { get; set; }
        public DateTime DateCreation { get; set; }
        public DateTime? DerniereSyncOsm { get; set; }
    }
}
