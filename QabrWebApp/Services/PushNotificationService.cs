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
            var dateLocale = priere.DateHeurePriere.AddMinutes(priere.UtcOffsetMinutes);

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

        public async Task ScheduleMosqueeReminderAsync(int mosqueeId, PriereJanaza priere)
        {
            var dateEnvoi = priere.DateHeurePriere.AddMinutes(-30);
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

        private const int ChunkSize = 100;
        private static readonly TimeSpan ChunkDelay = TimeSpan.FromMilliseconds(100);

        private async Task SendBatchAsync<T>(IEnumerable<T> messages)
        {
            var chunks = messages.Chunk(ChunkSize).ToList();
            for (int i = 0; i < chunks.Count; i++)
            {
                try
                {
                    var content = new StringContent(JsonSerializer.Serialize(chunks[i], _json), Encoding.UTF8, "application/json");
                    var response = await _http.PostAsync("https://exp.host/--/api/v2/push/send", content);
                    var responseBody = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                        _logger.LogWarning("Expo Push API returned {StatusCode} (chunk {i}/{total}): {body}", response.StatusCode, i + 1, chunks.Count, responseBody);
                    else
                        _logger.LogInformation("Expo Push chunk {i}/{total} OK: {body}", i + 1, chunks.Count, responseBody);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de l'envoi du chunk {i}/{total}", i + 1, chunks.Count);
                }

                if (i < chunks.Count - 1)
                    await Task.Delay(ChunkDelay);
            }
        }
    }
}
