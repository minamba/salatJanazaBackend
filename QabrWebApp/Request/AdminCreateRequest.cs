using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.Request
{
    public class AdminCreateRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;
        [Required]
        public string Prenom { get; set; } = string.Empty;
        [Required]
        public string Nom { get; set; } = string.Empty;
        public string? Telephone { get; set; }
        public string Role { get; set; } = "User";
    }
}
