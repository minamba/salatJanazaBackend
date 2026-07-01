namespace QabrWebApp.Services
{
    public class MosqueeDeduplicationBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MosqueeDeduplicationBackgroundService> _logger;

        public MosqueeDeduplicationBackgroundService(IServiceScopeFactory scopeFactory, ILogger<MosqueeDeduplicationBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MosqueeDeduplicationBackgroundService démarré");

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeUntilNextSunday();
                _logger.LogInformation("Prochaine déduplication des mosquées : dimanche dans {h}h{m}min",
                    (int)delay.TotalHours, delay.Minutes);

                try
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (stoppingToken.IsCancellationRequested) break;

                _logger.LogInformation("Lancement de la déduplication hebdomadaire des mosquées");
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMosqueeDeduplicationService>();
                    var (groupes, supprimees) = await service.SupprimerDoublonsAsync(stoppingToken);
                    _logger.LogInformation(
                        "Déduplication terminée : {groupes} groupe(s), {supprimees} mosquée(s) supprimée(s)",
                        groupes, supprimees);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Erreur lors de la déduplication hebdomadaire");
                }
            }
        }

        // Calcule le délai jusqu'au prochain dimanche à 03h00 UTC
        private static TimeSpan TimeUntilNextSunday()
        {
            var now = DateTime.UtcNow;
            var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7;
            var nextRun = now.Date.AddDays(daysUntilSunday).AddHours(3);
            if (nextRun <= now) nextRun = nextRun.AddDays(7);
            return nextRun - now;
        }
    }
}
