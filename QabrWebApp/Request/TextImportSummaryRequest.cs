using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.Request
{
    public class TextImportSummaryRequest
    {
        [Required] public int Total { get; set; }
        [Required] public int Success { get; set; }
        [Required] public int Skipped { get; set; }
        public List<SkippedEntryRequest> SkippedEntries { get; set; } = [];
    }

    public class SkippedEntryRequest
    {
        [Required] public string MosqueeNom { get; set; } = string.Empty;
        public string? NomDefunt { get; set; }
        public DateTime DateHeurePriere { get; set; }
        [Required] public string Reason { get; set; } = string.Empty;
    }
}
