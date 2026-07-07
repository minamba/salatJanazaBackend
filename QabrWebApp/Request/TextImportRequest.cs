using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.Request
{
    public class TextImportRequest
    {
        [Required]
        [MinLength(1)]
        public string Text { get; set; } = string.Empty;
    }
}
