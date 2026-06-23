using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QabrWebApp.Dal.Entities
{
    [Table("RappelsPush")]
    public class RappelPush
    {
        [Key]
        public int Id { get; set; }
        public int MosqueeId { get; set; }
        public int PriereJanazaId { get; set; }
        public DateTime DateEnvoi { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? EnvoyeAt { get; set; }

        // Navigation vers PriereJanaza uniquement (cascade delete suffit)
        // MosqueeId est un plain int pour éviter les multiple cascade paths SQL Server
        [ForeignKey(nameof(PriereJanazaId))]
        public PriereJanaza? PriereJanaza { get; set; }
    }
}
