using Microsoft.EntityFrameworkCore;
using QabrWebApp.Dal.Entities;
using QabrWebApp.Domain.Repositories;

namespace QabrWebApp.Services
{
    public class RappelPushBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RappelPushBackgroundService> _logger;
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        public RappelPushBackgroundService(IServiceScopeFactory scopeFactory, ILogger<RappelPushBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RappelPushBackgroundService démarré — vérification toutes les {interval} min", Interval.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                await ProcessPendingRappelsAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
        }

        private async Task ProcessPendingRappelsAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var rappelRepo = scope.ServiceProvider.GetRequiredService<IRappelPushRepository>();
                var push = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

                var pending = await rappelRepo.GetPendingAsync();
                if (pending.Count == 0) return;

                _logger.LogInformation("RappelPush : {count} rappel(s) à envoyer", pending.Count);

                foreach (var rappel in pending)
                {
                    try
                    {
                        // Abonnés ET riverains : la même liste que pour la
                        // déclaration. Avant, ce service ne connaissait que les
                        // abonnés — quelqu'un ayant la mosquée dans son rayon
                        // apprenait le décès puis n'était jamais rappelé, alors
                        // que c'est le rappel qui permet de s'y rendre.
                        var allTokens = await push.GetDestinatairesMosqueeAsync(rappel.MosqueeId);

                        if (allTokens.Count > 0)
                        {
                            var db = scope.ServiceProvider.GetRequiredService<QabrWebAppDatabaseContext>();
                            var priere = await db.PrieresJanaza
                                .Include(p => p.Mosquee)
                                .AsNoTracking()
                                .FirstOrDefaultAsync(p => p.Id == rappel.PriereJanazaId, ct);

                            if (priere is not null)
                            {
                                var mosqueeNom = priere.Mosquee?.Nom ?? "une mosquée";
                                // Wall-clock UTC = heure locale telle qu'affichée. Ne pas ajouter l'offset.
                                var data = new { priereId = priere.Id, mosqueeId = rappel.MosqueeId };
                                foreach (var group in allTokens.GroupBy(x => x.Language))
                                {
                                    var (title, body) = NotifL10n.BuildReminderNotif(
                                        group.Key, priere.EstAnonyme, priere.NomDefunt, priere.Genre,
                                        mosqueeNom, priere.DateHeurePriere);
                                    await push.SendToTokensAsync(group.Select(x => x.Token), title, body, data);
                                }
                            }
                        }

                        await rappelRepo.MarkSentAsync(rappel.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erreur lors de l'envoi du rappel {rappelId}", rappel.Id);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Erreur dans RappelPushBackgroundService");
            }
        }
    }
}
