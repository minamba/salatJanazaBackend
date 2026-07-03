using QabrWebApp.Domain.Models;
using QabrWebApp.Domain.Repositories;
using System.Text;
using System.Text.Json;

namespace QabrWebApp.Services
{
    public class PushNotificationService : IPushNotificationService
    {
        private readonly HttpClient _http;
        private readonly IAbonnementRepository _abonnementRepo;
        private readonly IRappelPushRepository _rappelRepo;
        private readonly IUtilisateurTokenRepository _tokenRepo;
        private readonly ILogger<PushNotificationService> _logger;

        private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public PushNotificationService(HttpClient http, IAbonnementRepository abonnementRepo, IRappelPushRepository rappelRepo, IUtilisateurTokenRepository tokenRepo, ILogger<PushNotificationService> logger)
        {
            _http = http;
            _abonnementRepo = abonnementRepo;
            _rappelRepo = rappelRepo;
            _tokenRepo = tokenRepo;
            _logger = logger;
        }

        public async Task NotifyMosqueeSubscribersAsync(int mosqueeId, PriereJanaza priere)
        {
            var abonnes = await _abonnementRepo.GetByMosqueeIdAsync(mosqueeId);
            var userIds = abonnes.Select(a => a.UtilisateurId).Distinct().ToList();

            var newTokens = await _tokenRepo.GetTokensByUserIdsAsync(userIds);
            var legacyTokens = abonnes
                .Where(a => a.Utilisateur?.ExpoToken is not null)
                .Select(a => a.Utilisateur!.ExpoToken!)
                .Where(t => !newTokens.Contains(t));
            var tokens = newTokens.Concat(legacyTokens).Distinct().ToList();

            if (tokens.Count == 0) return;

            var mosqueeNom = priere.Mosquee?.Nom ?? "une mosquée";
            var defunt = (priere.EstAnonyme || string.IsNullOrEmpty(priere.NomDefunt))
                ? "Défunt anonyme"
                : priere.NomDefunt!;
            var genre = priere.Genre?.ToLower() switch {
                "homme" => "Homme", "femme" => "Femme", "enfant" => "Enfant", _ => null
            };
            // DateHeurePriere est stocké en wall-clock UTC (= heure locale telle qu'affichée).
            // Ne pas ajouter utcOffset : la valeur EST déjà l'heure locale.
            var dateLocale = priere.DateHeurePriere;

            var title = "🕌 Salat Janaza";
            var body = $"{defunt}{(genre is not null ? $" ({genre})" : "")} · {mosqueeNom} · {dateLocale:dd/MM à HH:mm}";

            var messages = tokens.Select(token => new
            {
                to = token,
                title,
                body,
                data = new { priereId = priere.Id, mosqueeId },
                sound = "default",
                // Android 8+ : le canal doit correspondre à celui créé sur l'appareil.
                // Sans channelId, FCM peut rejeter silencieusement la notification.
                channelId = "default",
                // priority "high" = FCM high priority → réveille l'appareil immédiatement.
                // Sans ça, Android peut retarder ou grouper les notifications.
                priority = "high",
            }).ToList();

            await SendBatchAsync(messages);
        }

        public async Task RescheduleMosqueeReminderAsync(int mosqueeId, PriereJanaza priere)
        {
            await _rappelRepo.DeletePendingByPriereIdAsync(priere.Id);
            await ScheduleMosqueeReminderAsync(mosqueeId, priere);
        }

        public async Task ScheduleMosqueeReminderAsync(int mosqueeId, PriereJanaza priere)
        {
            // DateHeurePriere est en wall-clock UTC (= heure locale).
            // Le vrai UTC de la prière = wall-clock - utcOffset.
            // Le rappel doit partir 30 min avant le vrai UTC.
            var trueUtcPrayer = priere.DateHeurePriere.AddMinutes(-priere.UtcOffsetMinutes);
            var dateEnvoi = trueUtcPrayer.AddMinutes(-30);
            if (dateEnvoi <= DateTime.UtcNow) return;

            await _rappelRepo.CreateAsync(new RappelPush
            {
                MosqueeId = mosqueeId,
                PriereJanazaId = priere.Id,
                DateEnvoi = dateEnvoi,
                CreatedAt = DateTime.UtcNow,
            });
        }

        public Task SendToTokensAsync(IEnumerable<string> tokens, string title, string body, object? data = null)
        {
            var messages = tokens.Select(token => new { to = token, title, body, data, sound = "default", channelId = "default", priority = "high" }).ToList();
            return SendBatchAsync(messages);
        }

        public async Task SendToTokenAsync(string expoToken, string title, string body, object? data = null)
        {
            var message = new { to = expoToken, title, body, data, sound = "default", channelId = "default", priority = "high" };
            await SendBatchAsync([message]);
        }

        public async Task SendPermissionUpdateAsync(string expoToken, bool canImportFlyer)
        {
            // Notification silencieuse : pas de titre/body/son → aucune UI côté utilisateur.
            // _contentAvailable réveille l'app iOS en arrière-plan.
            var message = new
            {
                to = expoToken,
                data = new { type = "PERMISSION_UPDATED", canImportFlyer },
                _contentAvailable = true,
                priority = "high",
            };
            await SendBatchAsync([message]);
        }

        public Task SendPermissionUpdateToManyAsync(IEnumerable<string> expoTokens, bool canImportFlyer)
        {
            // SendBatchAsync découpe automatiquement en chunks de 100
            var messages = expoTokens
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(token => new
                {
                    to = token,
                    data = new { type = "PERMISSION_UPDATED", canImportFlyer },
                    _contentAvailable = true,
                    priority = "high",
                });
            return SendBatchAsync(messages);
        }

        private const int ChunkSize = 100;
        private static readonly TimeSpan ChunkDelay = TimeSpan.FromMilliseconds(100);

        private async Task SendBatchAsync<T>(IEnumerable<T> messages)
        {
            // Itération lazy : un chunk est sérialisé et envoyé, puis libéré avant le suivant.
            // Jamais tous les chunks en mémoire simultanément.
            int chunkIndex = 0;
            bool first = true;
            foreach (var chunk in messages.Chunk(ChunkSize))
            {
                if (!first) await Task.Delay(ChunkDelay);
                first = false;
                chunkIndex++;
                try
                {
                    var content = new StringContent(JsonSerializer.Serialize(chunk, _json), Encoding.UTF8, "application/json");
                    var response = await _http.PostAsync("https://exp.host/--/api/v2/push/send", content);
                    var responseBody = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                        _logger.LogWarning("Expo Push API returned {StatusCode} (chunk {Index}): {body}", response.StatusCode, chunkIndex, responseBody);
                    else
                        _logger.LogInformation("Expo Push chunk {Index} OK", chunkIndex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de l'envoi du chunk {Index}", chunkIndex);
                }
            }
        }
    }
}
