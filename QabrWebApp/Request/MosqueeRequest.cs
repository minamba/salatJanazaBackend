using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.Request
{
    public class MosqueeRequest
    {
        [Required, MaxLength(200)]
        public string Nom { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Adresse { get; set; }

        [Required]
        public double Latitude { get; set; }

        [Required]
        public double Longitude { get; set; }

        [MaxLength(50)]
        public string? OsmId { get; set; }
    }

    public class MosqueeNearbyRequest
    {
        [Required]
        public double Latitude { get; set; }

        [Required]
        public double Longitude { get; set; }

        [Range(1, 40000)]
        public double RadiusKm { get; set; } = 5;
    }

    public class MosqueeSyncOsmRequest
    {
        public List<MosqueeSyncOsmItem> Mosques { get; set; } = [];
    }

    public class MosqueeSyncOsmItem
    {
        [Required, MaxLength(60)]
        public string OsmId { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string Nom { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Adresse { get; set; }

        [Required]
        public double Latitude { get; set; }

        [Required]
        public double Longitude { get; set; }
    }
}
