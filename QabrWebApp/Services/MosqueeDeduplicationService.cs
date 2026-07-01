using Microsoft.EntityFrameworkCore;
using QabrWebApp.Dal.Entities;

namespace QabrWebApp.Services
{
    public interface IMosqueeDeduplicationService
    {
        Task<(int groupesTraites, int mosqueesSupprimees)> SupprimerDoublonsAsync(CancellationToken ct = default);
    }

    public class MosqueeDeduplicationService : IMosqueeDeduplicationService
    {
        private readonly QabrWebAppDatabaseContext _db;
        private readonly ILogger<MosqueeDeduplicationService> _logger;

        public MosqueeDeduplicationService(QabrWebAppDatabaseContext db, ILogger<MosqueeDeduplicationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<(int groupesTraites, int mosqueesSupprimees)> SupprimerDoublonsAsync(CancellationToken ct = default)
        {
            // Coordonnées ayant plus d'une mosquée
            var coordsDoublons = await _db.Mosquees
                .GroupBy(m => new { m.Latitude, m.Longitude })
                .Where(g => g.Count() > 1)
                .Select(g => new { g.Key.Latitude, g.Key.Longitude })
                .ToListAsync(ct);

            int groupesTraites = 0;
            int totalSupprimees = 0;

            foreach (var coords in coordsDoublons)
            {
                var mosquees = await _db.Mosquees
                    .Where(m => m.Latitude == coords.Latitude && m.Longitude == coords.Longitude)
                    .OrderByDescending(m => m.DateCreation)
                    .ToListAsync(ct);

                if (mosquees.Count < 2) continue;

                var keeper = mosquees[0];
                var toDeleteIds = mosquees.Skip(1).Select(m => m.Id).ToList();

                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    // 1. Re-linker les prières vers la mosquée conservée
                    await _db.PrieresJanaza
                        .Where(p => toDeleteIds.Contains(p.MosqueeId))
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.MosqueeId, keeper.Id), ct);

                    // 2. Re-linker les abonnements en évitant les doublons (index unique UtilisateurId+MosqueeId)
                    var dejaAbonnesIds = await _db.Abonnements
                        .Where(a => a.MosqueeId == keeper.Id)
                        .Select(a => a.UtilisateurId)
                        .ToListAsync(ct);

                    // Supprimer les abonnements qui créeraient un doublon
                    await _db.Abonnements
                        .Where(a => toDeleteIds.Contains(a.MosqueeId) && dejaAbonnesIds.Contains(a.UtilisateurId))
                        .ExecuteDeleteAsync(ct);

                    // Re-linker les abonnements restants
                    await _db.Abonnements
                        .Where(a => toDeleteIds.Contains(a.MosqueeId))
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.MosqueeId, keeper.Id), ct);

                    // 3. Mettre à jour MosqueeId dans RappelsPush (plain int, pas de FK)
                    await _db.RappelsPush
                        .Where(r => toDeleteIds.Contains(r.MosqueeId))
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.MosqueeId, keeper.Id), ct);

                    // 4. Supprimer les mosquées en doublon
                    await _db.Mosquees
                        .Where(m => toDeleteIds.Contains(m.Id))
                        .ExecuteDeleteAsync(ct);

                    await tx.CommitAsync(ct);

                    groupesTraites++;
                    totalSupprimees += toDeleteIds.Count;

                    _logger.LogInformation(
                        "Doublon résolu : mosquée conservée {keepId} ({nom}), supprimées : [{ids}]",
                        keeper.Id, keeper.Nom, string.Join(", ", toDeleteIds));
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct);
                    _logger.LogError(ex, "Erreur lors de la déduplication du groupe Lat={lat} Lon={lon}", coords.Latitude, coords.Longitude);
                }
            }

            _logger.LogInformation(
                "Déduplication terminée : {groupes} groupe(s) traité(s), {total} mosquée(s) supprimée(s)",
                groupesTraites, totalSupprimees);

            return (groupesTraites, totalSupprimees);
        }
    }
}
