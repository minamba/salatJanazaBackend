using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.Dal.Entities
{
    public class AppSetting
    {
        [Key]
        [MaxLength(100)]
        public string Key { get; set; } = "";

        [MaxLength(1000)]
        public string? Value { get; set; }
    }
}
