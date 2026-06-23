using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.Request
{
    public class ContactRequest
    {
        [Required]
        public string Nom { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;
    }
}
