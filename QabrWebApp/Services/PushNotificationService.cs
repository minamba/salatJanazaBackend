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
        private readonly IUtilisateurRepository _utilisateurRepo;
        private readonly IMosqueeRepository _mosqueeRepo;
        private readonly ILogger<PushNotificationService> _logger;

        private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public PushNotificationService(HttpClient http, IAbonnementRepository abonnementRepo, IRappelPushRepository rappelRepo, IUtilisateurTokenRepository tokenRepo, IUtilisateurRepository utilisateurRepo, IMosqueeRepository mosqueeRepo, ILogger<PushNotificationService> logger)
        {
            _http = http;
            _abonnementRepo = abonnementRepo;
            _rappelRepo = rappelRepo;
            _tokenRepo = tokenRepo;
            _utilisateurRepo = utilisateurRepo;
            _mosqueeRepo = mosqueeRepo;
            _logger = logger;
        }

        public async Task NotifyMosqueeSubscribersAsync(int mosqueeId, PriereJanaza priere)
        {
            var abonnes = await _abonnementRepo.GetByMosqueeIdAsync(mosqueeId);
            var userIds = abonnes.Select(a => a.UtilisateurId).Distinct().ToList();

            var newTokens = await _tokenRepo.GetTokensWithLanguageByUserIdsAsync(userIds);
            var newTokenSet = newTokens.Select(x => x.Token).ToHashSet();
            var legacyTokens = abonnes
                .Where(a => a.Utilisateur?.ExpoToken is not null && !newTokenSet.Contains(a.Utilisateur.ExpoToken))
                .Select(a => (Token: a.Utilisateur!.ExpoToken!, Language: a.Utilisateur.Language ?? "fr"));
            var allTokens = newTokens.Concat(legacyTokens).DistinctBy(x => x.Token).ToList();

            if (allTokens.Count == 0) return;

            // DateHeurePriere est stocké en wall-clock UTC (= heure locale telle qu'affichée).
            var mosqueeNom = priere.Mosquee?.Nom ?? "une mosquée";
            var messages = allTokens.Select(tl =>
            {
                var (title, body) = NotifL10n.BuildJanazaNotif(
                    tl.Language, priere.EstAnonyme, priere.NomDefunt, priere.Genre,
                    mosqueeNom, priere.DateHeurePriere);
                return new
                {
                    to = tl.Token,
                    title,
                    body,
                    data = new { priereId = priere.Id, mosqueeId },
                    sound = "default",
                    channelId = "default",
                    priority = "high",
                };
            }).ToList();

            await SendBatchAsync(messages);
        }

        /// <summary>
        /// Qui doit entendre parler de cette mosquée : ses abonnés, PLUS les
        /// utilisateurs qui l'ont dans leur rayon sans y être abonnés.
        ///
        /// POURQUOI CETTE MÉTHODE EXISTE
        /// -----------------------------
        /// La règle était écrite deux fois, à deux endroits qui n'en
        /// connaissaient chacun qu'une moitié : la déclaration appelait les
        /// abonnés puis le rayon, le rappel n'appelait que les abonnés. Les
        /// riverains recevaient donc l'annonce du décès et jamais le rappel
        /// avant la prière. Une seule définition, un seul comportement.
        ///
        /// RÉSOLU À L'ENVOI, PAS À LA PROGRAMMATION
        /// ----------------------------------------
        /// Le rappel part trente minutes avant la prière et interroge les
        /// positions à ce moment-là. Quelqu'un qui s'est rapproché depuis la
        /// déclaration sera prévenu ; quelqu'un qui s'est éloigné ne le sera
        /// pas. C'est le comportement voulu : le rayon dit « puis-je m'y
        /// rendre maintenant », pas « où étais-je hier ».
        /// </summary>
        public async Task<IReadOnlyList<(string Token, string Language)>> GetDestinatairesMosqueeAsync(int mosqueeId)
        {
            var abonnes = await _abonnementRepo.GetByMosqueeIdAsync(mosqueeId);
            var abonneIds = abonnes.Select(a => a.UtilisateurId).ToHashSet();

            var jetonsAbonnes = await _tokenRepo.GetTokensWithLanguageByUserIdsAsync(abonneIds);
            var connus = jetonsAbonnes.Select(x => x.Token).ToHashSet();

            // Ancien champ ExpoToken porté par l'utilisateur lui-même : encore
            // le seul jeton de ceux qui n'ont pas rouvert l'application depuis
            // la table dédiée. Les ignorer reviendrait à cesser de les notifier.
            var legacyAbonnes = abonnes
                .Where(a => a.Utilisateur?.ExpoToken is not null && !connus.Contains(a.Utilisateur.ExpoToken))
                .Select(a => (Token: a.Utilisateur!.ExpoToken!, Language: a.Utilisateur.Language ?? "fr"));

            IEnumerable<(string Token, string Language)> jetonsRayon = [];

            var mosquee = await _mosqueeRepo.GetByIdAsync(mosqueeId);
            if (mosquee is null)
            {
                // Les abonnés restent joignables : leur lien ne dépend pas des
                // coordonnées. On perd le rayon, et on le dit.
                _logger.LogWarning(
                    "[Destinataires] MosqueeId={MosqueeId} introuvable — seuls les abonnés seront notifiés", mosqueeId);
            }
            else
            {
                // Les abonnés sont exclus de la requête : ils sont déjà dans la
                // liste, et les compter ici ferait deux fois le travail.
                var riverains = await _utilisateurRepo.GetUsersInRadiusAsync(
                    mosquee.Latitude, mosquee.Longitude, abonneIds);

                var jetonsRiverains = await _tokenRepo.GetTokensWithLanguageByUserIdsAsync(
                    riverains.Select(u => u.UserId));
                var connusRiverains = jetonsRiverains.Select(x => x.Token).ToHashSet();

                var legacyRiverains = riverains
                    .Where(u => u.LegacyToken is not null && !connusRiverains.Contains(u.LegacyToken))
                    .Select(u => (Token: u.LegacyToken!, Language: u.Language));

                jetonsRayon = jetonsRiverains.Concat(legacyRiverains);
            }

            // Le dédoublonnage final est indispensable et non redondant : un
            // même appareil peut porter un jeton moderne et un jeton hérité, et
            // rien n'empêche deux comptes de partager un téléphone.
            var tous = jetonsAbonnes
                .Concat(legacyAbonnes)
                .Concat(jetonsRayon)
                .DistinctBy(x => x.Token)
                .ToList();

            _logger.LogInformation(
                "[Destinataires] MosqueeId={MosqueeId} → {Total} jeton(s) : {Abonnes} via abonnement, {Rayon} via rayon",
                mosqueeId, tous.Count, jetonsAbonnes.Count, jetonsRayon.Count());

            return tous;
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
            if (trueUtcPrayer <= DateTime.UtcNow) return; // prière déjà passée

            var dateEnvoi = trueUtcPrayer.AddMinutes(-30);
            // Si la fenêtre des 30 min est déjà passée mais la prière est encore à venir,
            // envoyer le rappel dans 30 secondes (cas d'une modification de dernière minute)
            if (dateEnvoi <= DateTime.UtcNow)
                dateEnvoi = DateTime.UtcNow.AddSeconds(30);

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

        public async Task NotifyRadiusUsersAsync(int mosqueeId, PriereJanaza priere)
        {
            var mosquee = await _mosqueeRepo.GetByIdAsync(mosqueeId);
            if (mosquee is null)
            {
                _logger.LogWarning("[Radius] MosqueeId={MosqueeId} introuvable — notification annulée", mosqueeId);
                return;
            }

            // Exclure les abonnés : ils reçoivent déjà la notif + rappel via NotifyMosqueeSubscribersAsync
            var abonnes = await _abonnementRepo.GetByMosqueeIdAsync(mosqueeId);
            var subscriberIds = abonnes.Select(a => a.UtilisateurId).ToHashSet();

            var radiusUsers = await _utilisateurRepo.GetUsersInRadiusAsync(
                mosquee.Latitude, mosquee.Longitude, subscriberIds);

            _logger.LogInformation("[Radius] MosqueeId={MosqueeId} → {Count} utilisateur(s) dans le rayon (abonnés exclus: {ExcludedCount})",
                mosqueeId, radiusUsers.Count, subscriberIds.Count);

            if (radiusUsers.Count == 0) return;

            var userIds = radiusUsers.Select(u => u.UserId).ToList();
            var newTokens = await _tokenRepo.GetTokensWithLanguageByUserIdsAsync(userIds);
            var newTokenSet = newTokens.Select(x => x.Token).ToHashSet();
            var legacyTokens = radiusUsers
                .Where(u => u.LegacyToken is not null && !newTokenSet.Contains(u.LegacyToken))
                .Select(u => (Token: u.LegacyToken!, Language: u.Language));
            var allTokens = newTokens.Concat(legacyTokens).DistinctBy(x => x.Token).ToList();

            if (allTokens.Count == 0)
            {
                _logger.LogWarning("[Radius] MosqueeId={MosqueeId} → {UserCount} user(s) trouvés mais aucun token Expo valide", mosqueeId, radiusUsers.Count);
                return;
            }

            _logger.LogInformation("[Radius] MosqueeId={MosqueeId} → envoi à {TokenCount} token(s)", mosqueeId, allTokens.Count);

            var mosqueeNom = priere.Mosquee?.Nom ?? mosquee.Nom ?? "une mosquée";
            var messages = allTokens.Select(tl =>
            {
                var (title, body) = NotifL10n.BuildJanazaNotif(
                    tl.Language, priere.EstAnonyme, priere.NomDefunt, priere.Genre,
                    mosqueeNom, priere.DateHeurePriere);
                return new
                {
                    to = tl.Token,
                    title,
                    body,
                    data = new { priereId = priere.Id, mosqueeId },
                    sound = "default",
                    channelId = "default",
                    priority = "high",
                };
            });

            await SendBatchAsync(messages);
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
