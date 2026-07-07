using System.Collections.Concurrent;

namespace QabrWebApp.Services
{
    // Accumulates per-prayer results as ImportText is called, then marks ready on Finalize().
    public class TextImportSummaryService : ITextImportSummaryService
    {
        private class Accumulator
        {
            public int Success;
            public int Skipped;
            public readonly List<SkippedEntry> SkippedEntries = [];
            public bool Ready;
            public DateTime CreatedAt = DateTime.UtcNow;
        }

        private readonly ConcurrentDictionary<string, Accumulator> _store = new();

        private Accumulator GetOrCreate(string token)
            => _store.GetOrAdd(token, _ => new Accumulator());

        public void AddSuccess(string token, string mosqueeNom)
        {
            var acc = GetOrCreate(token);
            lock (acc) { acc.Success++; }
        }

        public void AddSkip(string token, string mosqueeNom, string? nomDefunt, DateTime date, string reason)
        {
            var acc = GetOrCreate(token);
            lock (acc)
            {
                acc.Skipped++;
                acc.SkippedEntries.Add(new SkippedEntry(mosqueeNom, nomDefunt, date, reason));
            }
        }

        public void Finalize(string token)
        {
            var acc = GetOrCreate(token);
            lock (acc) { acc.Ready = true; }
        }

        public TextImportSummary? Get(string token)
        {
            if (!_store.TryGetValue(token, out var acc)) return null;
            lock (acc)
            {
                return new TextImportSummary(
                    Total: acc.Success + acc.Skipped,
                    Success: acc.Success,
                    Skipped: acc.Skipped,
                    SkippedEntries: [.. acc.SkippedEntries],
                    Ready: acc.Ready,
                    CreatedAt: acc.CreatedAt
                );
            }
        }
    }
}
