namespace QabrWebApp.IdentityServer.Helpers
{
    internal static class TelegramHelper
    {
        internal static async Task SendNewUserAsync(IConfiguration config, string prenom, string nom, string email, string lang)
        {
            var botToken = config["TelegramNewUser:BotToken"];
            var chatId   = config["TelegramNewUser:ChatId"];
            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId)) return;

            var now  = DateTime.UtcNow;
            var text = $"✅ *Nouvel utilisateur inscrit*\n" +
                       $"— Prénom : {Escape(prenom)}\n" +
                       $"— Nom : {Escape(nom)}\n" +
                       $"— Email : {Escape(email)}\n" +
                       $"— Langue : {lang}\n" +
                       $"— Date : {now:dd/MM/yyyy} à {now:HH:mm} UTC";

            using var client = new System.Net.Http.HttpClient();
            var payload = new Dictionary<string, string>
            {
                { "chat_id",    chatId       },
                { "text",       text         },
                { "parse_mode", "MarkdownV2" }
            };
            await client.PostAsync(
                $"https://api.telegram.org/bot{botToken}/sendMessage",
                new System.Net.Http.FormUrlEncodedContent(payload));
        }

        internal static string Escape(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "—";
            return s.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[")
                    .Replace("]", "\\]").Replace("~", "\\~").Replace("`", "\\`")
                    .Replace(">", "\\>").Replace("#", "\\#").Replace("+", "\\+")
                    .Replace("-", "\\-").Replace("=", "\\=").Replace("|", "\\|")
                    .Replace("{", "\\{").Replace("}", "\\}").Replace(".", "\\.")
                    .Replace("!", "\\!");
        }
    }
}
