using System.Text.Json.Serialization;

namespace QabrWebApp.ViewModels
{
    public class UtilisateurViewModel
    {
        public int Id { get; set; }
        public string IdentityUserId { get; set; } = string.Empty;
        public string Prenom { get; set; } = string.Empty;
        public string Nom { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Telephone { get; set; }
        public string? AdresseDomicile { get; set; }
        public double? LatitudeDomicile { get; set; }
        public double? LongitudeDomicile { get; set; }
        public int RayonNotification { get; set; }
        public bool NotifMouvement { get; set; }
        public DateTime DateInscription { get; set; }

        [JsonPropertyName("_role")]
        public string Role { get; set; } = "User";
        public string Language { get; set; } = "fr";
    }
}
