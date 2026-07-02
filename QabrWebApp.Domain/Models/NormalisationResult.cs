namespace QabrWebApp.Domain.Models
{
    public class NormalisationResult
    {
        public List<RenommeEntry> Renommes { get; } = new();
        public List<ActionEntry> Supprimes { get; } = new();
        public List<ActionEntry> Ignores { get; } = new();

        public record RenommeEntry(int Id, string AncienNom, string NouveauNom, string? Adresse);
        public record ActionEntry(int Id, string? Adresse, string Raison);
    }
}
