using Microsoft.AspNetCore.Identity;

namespace QabrWebApp.IdentityServer.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string Prenom { get; set; } = string.Empty;
        public string Nom { get; set; } = string.Empty;
        public string Language { get; set; } = "fr";
    }
}
