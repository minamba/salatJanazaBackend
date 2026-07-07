namespace QabrWebApp.Services
{
    public record SkippedEntry(string MosqueeNom, string? NomDefunt, DateTime DateHeurePriere, string Reason);

    public record TextImportSummary(
        int Total,
        int Success,
        int Skipped,
        List<SkippedEntry> SkippedEntries,
        bool Ready,
        DateTime CreatedAt
    );

    public interface ITextImportSummaryService
    {
        void AddSuccess(string token, string mosqueeNom);
        void AddSkip(string token, string mosqueeNom, string? nomDefunt, DateTime date, string reason);
        void Finalize(string token);
        TextImportSummary? Get(string token);
    }
}
