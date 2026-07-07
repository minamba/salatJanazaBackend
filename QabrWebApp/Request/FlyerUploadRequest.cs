using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.Request
{
    public class FlyerUploadRequest
    {
        [Required]
        public IFormFile File { get; set; } = null!;

        [Required]
        public int UtilisateurId { get; set; }

        public string? ExpoPushToken { get; set; }
    }
}
