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
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
        }

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
    }
}
