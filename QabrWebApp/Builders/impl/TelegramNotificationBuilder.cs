using QabrWebApp.Domain.Models;
using System.Text;

namespace QabrWebApp.Builders.impl
{
    public class TelegramNotificationBuilder : ITelegramNotificationBuilder
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TelegramNotificationBuilder> _logger;
        private readonly string? _janazaToken;
        private readonly string? _janazaChatId;
        private readonly string? _pendingToken;
        private readonly string? _pendingChatId;
        private readonly string? _commentaireToken;
        private readonly string? _commentaireChatId;

        public TelegramNotificationBuilder(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<TelegramNotificationBuilder> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _janazaToken        = config["TelegramNewJanaza:BotToken"];
            _janazaChatId       = config["TelegramNewJanaza:ChatId"];
            _pendingToken       = config["TelegramPending:BotToken"];
            _pendingChatId      = config["TelegramPending:ChatId"];
            _commentaireToken   = config["TelegramCommentaire:BotToken"];
            _commentaireChatId  = config["TelegramCommentaire:ChatId"];
        }

        public async Task NotifyNewJanazaAsync(PriereJanaza priere, string mosqueeNom, string? mosqueeAdresse, Utilisateur? utilisateur)
        {
            var sb = new StringBuilder();
            sb.AppendLine("🕌 *Nouvelle Janaza ajoutée*");
            sb.AppendLine($"— Défunt\\(e\\) : {EscapeMd(priere.EstAnonyme ? "Anonyme" : (priere.NomDefunt ?? "—"))}");
            sb.AppendLine($"— Genre : {FormatGenre(priere.Genre)}");
            sb.AppendLine($"— Prière le : {priere.DateHeurePriere:dd/MM/yyyy} à {priere.DateHeurePriere:HH:mm}");
            sb.AppendLine($"— Mosquée : {EscapeMd(mosqueeNom)}");
            if (!string.IsNullOrWhiteSpace(mosqueeAdresse))
                sb.AppendLine($"— Adresse : {EscapeMd(mosqueeAdresse)}");
            if (!string.IsNullOrWhiteSpace(priere.Commentaire))
                sb.AppendLine($"— Commentaire : {EscapeMd(priere.Commentaire)}");
            sb.AppendLine("———————————————————————");
            sb.AppendLine("👤 *Déclaré par :*");
            if (utilisateur != null)
            {
                sb.AppendLine($"— Nom : {EscapeMd($"{utilisateur.Prenom} {utilisateur.Nom}".Trim())}");
                sb.AppendLine($"— Email : {EscapeMd(utilisateur.Email ?? "—")}");
            }
            else
            {
                sb.AppendLine("— Utilisateur inconnu");
            }
            sb.AppendLine($"— Déclaré le : {DateTime.UtcNow:dd/MM/yyyy} à {DateTime.UtcNow:HH:mm} UTC");

            await SendAsync(_janazaToken, _janazaChatId, sb.ToString());
        }

        public async Task NotifyPendingJanazaAsync(PriereJanaza priere, string mosqueeNom)
        {
            var sb = new StringBuilder();
            sb.AppendLine("⏳ *Janaza en attente de validation*");
            sb.AppendLine($"— Défunt\\(e\\) : {EscapeMd(priere.EstAnonyme ? "Anonyme" : (priere.NomDefunt ?? "—"))}");
            sb.AppendLine($"— Genre : {FormatGenre(priere.Genre)}");
            sb.AppendLine($"— Prière le : {priere.DateHeurePriere:dd/MM/yyyy} à {priere.DateHeurePriere:HH:mm}");
            sb.AppendLine($"— Mosquée : {EscapeMd(mosqueeNom)} \\(en attente de validation\\)");

            await SendAsync(_pendingToken, _pendingChatId, sb.ToString());
        }

        public async Task NotifyPendingMosqueeAsync(Mosquee mosquee, string? utilisateurEmail)
        {
            var sb = new StringBuilder();
            sb.AppendLine("🕌 *Mosquée en attente de validation*");
            sb.AppendLine($"— Nom : {EscapeMd(mosquee.Nom)}");
            if (!string.IsNullOrWhiteSpace(mosquee.Adresse))
                sb.AppendLine($"— Adresse : {EscapeMd(mosquee.Adresse)}");
            sb.AppendLine($"— Coordonnées : {EscapeMd(mosquee.Latitude.ToString())} , {EscapeMd(mosquee.Longitude.ToString())}");
            if (!string.IsNullOrWhiteSpace(utilisateurEmail))
                sb.AppendLine($"— Soumis par : {EscapeMd(utilisateurEmail)}");

            await SendAsync(_pendingToken, _pendingChatId, sb.ToString());
        }

        public async Task NotifyNewCommentaireAsync(string nomDefunt, string? auteurNom, string contenu, bool isReply)
        {
            var sb = new StringBuilder();
            sb.AppendLine(isReply ? "💬 *Nouvelle réponse à un commentaire*" : "💬 *Nouveau commentaire*");
            sb.AppendLine($"— Défunt\\(e\\) : {EscapeMd(nomDefunt)}");
            sb.AppendLine($"— Auteur : {EscapeMd(auteurNom ?? "Anonyme")}");
            sb.AppendLine($"— Message : {EscapeMd(contenu)}");
            sb.AppendLine($"— Le : {DateTime.UtcNow:dd/MM/yyyy} à {DateTime.UtcNow:HH:mm} UTC");

            await SendAsync(_commentaireToken, _commentaireChatId, sb.ToString());
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static string FormatGenre(string? genre) => genre?.ToLowerInvariant() switch
        {
            "homme"  => "Homme",
            "femme"  => "Femme",
            "enfant" => "Enfant",
            _        => "Non précisé"
        };

        // Échappe les caractères spéciaux Telegram MarkdownV2
        private static string EscapeMd(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "—";
            return s.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[")
                    .Replace("]", "\\]").Replace("(", "\\(").Replace(")", "\\)")
                    .Replace("~", "\\~").Replace("`", "\\`")
                    .Replace(">", "\\>").Replace("#", "\\#").Replace("+", "\\+")
                    .Replace("-", "\\-").Replace("=", "\\=").Replace("|", "\\|")
                    .Replace("{", "\\{").Replace("}", "\\}").Replace(".", "\\.")
                    .Replace("!", "\\!");
        }

        private async Task SendAsync(string? botToken, string? chatId, string text)
        {
            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId)) return;
            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
                var payload = new Dictionary<string, string>
                {
                    { "chat_id", chatId },
                    { "text",    text   },
                    { "parse_mode", "MarkdownV2" }
                };
                var response = await client.PostAsync(url, new FormUrlEncodedContent(payload));
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Telegram SendAsync failed — status {Status}, body: {Body}", (int)response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telegram SendAsync exception — chatId: {ChatId}", chatId);
            }
        }
    }
}
