namespace QabrWebApp.Services
{
    internal static class NotifL10n
    {
        private sealed record Strings(
            string Anonymous,
            string Male,
            string Female,
            string Child,
            string ReminderTitle,
            string At,
            string DateFormat);

        // DateFormat : format C# pour la partie date du corps de notif.
        // Separateur et ordre varient selon la langue.
        // ja/ko utilisent des suffixes caracteres (月/日, 월/일) sans separateur numerique.
        private static readonly Dictionary<string, Strings> _map = new()
        {
            ["fr"] = new("Défunt anonyme",    "Homme",    "Femme",      "Enfant",      "⏰ Rappel — Salat Janaza dans 30 min",         "à",    "dd/MM"),
            ["en"] = new("Anonymous",               "Man",      "Woman",      "Child",       "⏰ Reminder — Salat Janaza in 30 min",          "at",        "MM/dd"),
            ["ar"] = new("متوفى مجهول", "رجل", "امرأة", "طفل", "⏰ تذكير — صلاة الجنازة خلال 30 دقيقة", "في", "dd/MM"),
            ["tr"] = new("Anonim",                  "Erkek",    "Kadın",  "Çocuk",  "⏰ Hatırlatma — Cenaze Namazı 30 dak. sonra", "saat", "dd.MM"),
            ["ja"] = new("匿名",             "男性", "女性", "子供", "⏰ リマインダー — ジャナザの礼拝まで30分", "", "M月d日"),
            ["ko"] = new("익명",             "남성", "여성", "어린이", "⏰ 알림 — 장례 예배 30분 전", "", "M월 d일"),
            ["ms"] = new("Ahli komuniti",            "Lelaki",   "Perempuan",  "Kanak-kanak", "⏰ Peringatan — Solat Jenazah dalam 30 min",     "pukul",     "dd/MM"),
            ["ur"] = new("گمنام متوفی", "مرد", "عورت", "بچہ", "⏰ یاددہانی — نماز جنازہ 30 منٹ میں", "بجے", "dd/MM"),
            ["id"] = new("Anonim",                  "Pria",     "Wanita",     "Anak",        "⏰ Pengingat — Salat Jenazah dalam 30 menit",    "pukul",     "dd/MM"),
            ["bn"] = new("অজ্ঞাত",  "পুরুষ", "মহিলা", "শিশু", "⏰ স্মরণিকা — জানাজার নামাজ ৩০ মিনিটে", "এ", "dd/MM"),
            ["ru"] = new("Аноним", "Мужчина", "Женщина", "Ребёнок", "⏰ Напоминание — Намаз Джаназа через 30 мин", "в", "dd.MM"),
            ["pt"] = new("Defunto anônimo",    "Homem",    "Mulher",     "Criança", "⏰ Lembrete — Salat Janaza em 30 min",          "às",   "dd/MM"),
            ["de"] = new("Unbekannte Person",        "Mann",     "Frau",       "Kind",        "⏰ Erinnerung — Totengebet in 30 Min.",          "um",        "dd.MM."),
            ["it"] = new("Defunto anonimo",          "Uomo",     "Donna",      "Bambino",     "⏰ Promemoria — Salat Janaza tra 30 min",        "alle",      "dd/MM"),
            ["es"] = new("Difunto anónimo",     "Hombre",   "Mujer",      "Niño",   "⏰ Recordatorio — Salat Janaza en 30 min",       "a las",     "dd/MM"),
        };

        private static Strings Get(string? lang) =>
            _map.TryGetValue(lang ?? "fr", out var s) ? s : _map["fr"];

        // Parses "NOM" "PRÉNOM" quoted storage format → "NOM PRÉNOM" without quotes.
        private static string FormatDisplayName(string? nomDefunt)
        {
            if (string.IsNullOrEmpty(nomDefunt)) return "";
            var matches = System.Text.RegularExpressions.Regex.Matches(nomDefunt, "\"([^\"]+)\"");
            if (matches.Count > 0)
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (System.Text.RegularExpressions.Match m in matches)
                    parts.Add(m.Groups[1].Value.Trim());
                return string.Join(" ", parts);
            }
            return nomDefunt.Trim();
        }

        public static (string Title, string Body) BuildJanazaNotif(
            string? lang, bool isAnonymous, string? nomDefunt, string? genre,
            string mosqueeNom, DateTime dateLocale)
        {
            var s = Get(lang);
            var defunt = (isAnonymous || string.IsNullOrEmpty(nomDefunt)) ? s.Anonymous : FormatDisplayName(nomDefunt);
            var genreLabel = genre?.ToLower() switch {
                "homme" => s.Male, "femme" => s.Female, "enfant" => s.Child, _ => null
            };
            var date = dateLocale.ToString(s.DateFormat);
            var time = dateLocale.ToString("HH:mm");
            var at = string.IsNullOrEmpty(s.At) ? " " : $" {s.At} ";
            var body = $"{defunt}{(genreLabel is not null ? $" ({genreLabel})" : "")} · {mosqueeNom} · {date}{at}{time}";
            return ("\U0001f54c Salat Janaza", body);
        }

        public static (string Title, string Body) BuildReminderNotif(
            string? lang, bool isAnonymous, string? nomDefunt, string? genre,
            string mosqueeNom, DateTime dateLocale)
        {
            var s = Get(lang);
            var defunt = (isAnonymous || string.IsNullOrEmpty(nomDefunt)) ? s.Anonymous : FormatDisplayName(nomDefunt);
            var genreLabel = genre?.ToLower() switch {
                "homme" => s.Male, "femme" => s.Female, "enfant" => s.Child, _ => null
            };
            var time = dateLocale.ToString("HH:mm");
            var body = $"{defunt}{(genreLabel is not null ? $" ({genreLabel})" : "")} · {mosqueeNom} · {time}";
            return (s.ReminderTitle, body);
        }
    }
}
