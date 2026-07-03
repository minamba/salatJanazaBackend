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
                var abonnementRepo = scope.ServiceProvider.GetRequiredService<IAbonnementRepository>();
                var tokenRepo = scope.ServiceProvider.GetRequiredService<IUtilisateurTokenRepository>();
                var push = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

                var pending = await rappelRepo.GetPendingAsync();
                if (pending.Count == 0) return;

                _logger.LogInformation("RappelPush : {count} rappel(s) à envoyer", pending.Count);

                foreach (var rappel in pending)
                {
                    try
                    {
                        var abonnes = await abonnementRepo.GetByMosqueeIdAsync(rappel.MosqueeId);
                        var userIds = abonnes.Select(a => a.UtilisateurId).Distinct().ToList();
                        var newTokens = await tokenRepo.GetTokensByUserIdsAsync(userIds);
                        var legacyTokens = abonnes
                            .Where(a => a.Utilisateur?.ExpoToken is not null)
                            .Select(a => a.Utilisateur!.ExpoToken!)
                            .Where(t => !newTokens.Contains(t));
                        var tokens = newTokens.Concat(legacyTokens).Distinct().ToList();

                        if (tokens.Count > 0)
                        {
                            var db = scope.ServiceProvider.GetRequiredService<QabrWebAppDatabaseContext>();
                            var priere = await db.PrieresJanaza
                                .Include(p => p.Mosquee)
                                .AsNoTracking()
                                .FirstOrDefaultAsync(p => p.Id == rappel.PriereJanazaId, ct);

                            if (priere is not null)
                            {
                                var mosqueeNom = priere.Mosquee?.Nom ?? "une mosquée";
                                var defunt = (priere.EstAnonyme || string.IsNullOrEmpty(priere.NomDefunt))
                                    ? "Défunt anonyme"
                                    : priere.NomDefunt!;
                                var genre = priere.Genre?.ToLower() switch {
                                    "homme" => "Homme", "femme" => "Femme", "enfant" => "Enfant", _ => null
                                };
                                // Wall-clock UTC = heure locale telle qu'affichée. Ne pas ajouter l'offset.
                                var dateLocale = priere.DateHeurePriere;

                                var title = "⏰ Rappel — Salat Janaza dans 30 min";
                                var body = $"{defunt}{(genre is not null ? $" ({genre})" : "")} · {mosqueeNom} · {dateLocale:HH:mm}";

                                await push.SendToTokensAsync(tokens, title, body, new { priereId = priere.Id, mosqueeId = rappel.MosqueeId });
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
