namespace QabrWebApp.Domain.Models
{
    public class RappelPush
    {
        public int Id { get; set; }
        public int MosqueeId { get; set; }
        public int PriereJanazaId { get; set; }
        public DateTime DateEnvoi { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? EnvoyeAt { get; set; }
    }
}
