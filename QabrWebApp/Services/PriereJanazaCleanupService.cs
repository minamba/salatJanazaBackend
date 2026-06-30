using Microsoft.EntityFrameworkCore;
using QabrWebApp.Dal.Entities;

namespace QabrWebApp.Services
{
    public class PriereJanazaCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PriereJanazaCleanupService> _logger;
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan GracePeriod = TimeSpan.FromHours(2);
        private static readonly TimeSpan MonthlyArchiveAge = TimeSpan.FromDays(21); // 3 semaines

        // Mémorise le (année, mois) de la dernière purge mensuelle pour ne la déclencher qu'une fois.
        private (int Year, int Month) _lastMonthlyPurge = (0, 0);

        public PriereJanazaCleanupService(IServiceScopeFactory scopeFactory, ILogger<PriereJanazaCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PriereJanazaCleanupService démarré — purge toutes les {interval} min", Interval.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                await PurgeExpiredAsync(stoppingToken);
                await MaybePurgeMonthlyArchiveAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
        }

        // Purge toutes les 30 min : supprime les prières passées depuis plus de 2h.
        private async Task PurgeExpiredAsync(CancellationToken ct)
        {
            try
            {
                var cutoff = DateTime.UtcNow - GracePeriod;
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<QabrWebAppDatabaseContext>();

                var expired = await db.PrieresJanaza
                    .Where(p => p.DateHeurePriere < cutoff)
                    .ToListAsync(ct);

                if (expired.Count == 0) return;

                db.PrieresJanaza.RemoveRange(expired);
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Purge : {count} prière(s) supprimée(s) (antérieures à {cutoff:u})", expired.Count, cutoff);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Erreur lors de la purge des prières expirées");
            }
        }

        // Purge mensuelle le 1er de chaque mois : supprime toutes les janazas
        // dont la DateCreation remonte à plus de 3 semaines.
        private async Task MaybePurgeMonthlyArchiveAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            if (now.Day != 1) return;
            if (_lastMonthlyPurge == (now.Year, now.Month)) return;

            try
            {
                var cutoff = now - MonthlyArchiveAge;
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<QabrWebAppDatabaseContext>();

                var old = await db.PrieresJanaza
                    .Where(p => p.DateCreation < cutoff)
                    .ToListAsync(ct);

                if (old.Count > 0)
                {
                    db.PrieresJanaza.RemoveRange(old);
                    await db.SaveChangesAsync(ct);
                }

                _lastMonthlyPurge = (now.Year, now.Month);
                _logger.LogInformation(
                    "Purge mensuelle (1er du mois) : {count} janaza(s) supprimée(s) (créées avant {cutoff:u})",
                    old.Count, cutoff);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Erreur lors de la purge mensuelle des janazas archivées");
            }
        }
    }
}
