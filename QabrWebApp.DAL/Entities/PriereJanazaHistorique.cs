using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QabrWebApp.Dal.Entities
{
    [Table("PrieresJanazaHistorique")]
    public class PriereJanazaHistorique
    {
        [Key]
        public int Id { get; set; }

        public DateTime DateCreation { get; set; }

        [MaxLength(10)]
        public string? Genre { get; set; }

        [MaxLength(200)]
        public string? NomDefunt { get; set; }

        public bool EstAnonyme { get; set; }

        // Dénormalisé : les données restent même si l'utilisateur ou la mosquée est supprimé
        [MaxLength(100)]
        public string? DeclarantPrenom { get; set; }

        [MaxLength(100)]
        public string? DeclarantNom { get; set; }

        [MaxLength(300)]
        public string? MosqueeNom { get; set; }

        [MaxLength(100)]
        public string? Pays { get; set; }

        [MaxLength(200)]
        public string? VilleEnterrement { get; set; }
    }
}
