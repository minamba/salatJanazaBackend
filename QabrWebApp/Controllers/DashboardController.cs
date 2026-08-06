using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QabrWebApp.Dal.Entities;

namespace QabrWebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly QabrWebAppDatabaseContext _db;

        public DashboardController(QabrWebAppDatabaseContext db) => _db = db;

        private class SeriesPointDto { public string label { get; set; } = ""; public int value { get; set; } }
        private class PaysCountDto   { public string pays  { get; set; } = ""; public int count { get; set; } }
        private class MosqueeCountDto{ public string nom   { get; set; } = ""; public int count { get; set; } }

        private class DeclDetailDto { public string? nomDefunt { get; set; } public bool estAnonyme { get; set; } public string? genre { get; set; } public string heureDeclaration { get; set; } = ""; public string? declarantPrenom { get; set; } public string? declarantNom { get; set; } }
        private class UserDetailDto  { public string prenom { get; set; } = ""; public string nom { get; set; } = ""; }

        [HttpGet("slot-details")]
        public async Task<IActionResult> GetSlotDetails(
            [FromQuery] string  period           = "jour",
            [FromQuery] string? date             = null,
            [FromQuery] int     utcOffsetMinutes = 0,
            [FromQuery] int     slotIndex        = 0,
            [FromQuery] string  type             = "declarations")
        {
            if (!DateTime.TryParse(date, out var refDate))
                refDate = DateTime.UtcNow.AddMinutes(utcOffsetMinutes).Date;

            // localStart/localEnd = bornes dans le fuseau du client
            DateTime localStart, localEnd;
            switch (period)
            {
                case "semaine":
                    int dow = ((int)refDate.DayOfWeek + 6) % 7;
                    localStart = refDate.Date.AddDays(-dow);
                    localEnd   = localStart.AddDays(7);
                    break;
                case "mois":
                    localStart = new DateTime(refDate.Year, refDate.Month, 1);
                    localEnd   = localStart.AddMonths(1);
                    break;
                case "annee":
                    localStart = new DateTime(refDate.Year, 1, 1);
                    localEnd   = new DateTime(refDate.Year + 1, 1, 1);
                    break;
                default:
                    localStart = refDate.Date;
                    localEnd   = localStart.AddDays(1);
                    break;
            }

            // Conversion en UTC pour les requêtes SQL (DateCreation stockée en UTC)
            DateTime start = localStart.AddMinutes(-utcOffsetMinutes);
            DateTime end   = localEnd.AddMinutes(-utcOffsetMinutes);

            DateTime ToLocal(DateTime utc) => utc.AddMinutes(utcOffsetMinutes);

            bool InSlot(DateTime utcCreation)
            {
                var local = ToLocal(utcCreation);
                return period switch
                {
                    "jour"    => local.Hour == slotIndex,
                    "semaine" => local.Date == start.AddDays(slotIndex).Date,
                    "mois"    => local.Date == start.AddDays(slotIndex).Date,
                    "annee"   => local.Month == slotIndex + 1,
                    _         => false
                };
            }

            if (type == "utilisateurs")
            {
                var users = await _db.Utilisateurs.AsNoTracking()
                    .Where(u => u.DateInscription >= start && u.DateInscription < end)
                    .Select(u => new { u.Prenom, u.Nom, u.DateInscription })
                    .ToListAsync();

                var result = users
                    .Where(u => InSlot(u.DateInscription))
                    .Select(u => new UserDetailDto { prenom = u.Prenom, nom = u.Nom });

                return Ok(result);
            }
            else
            {
                // Historique preferred (has declarant name); live fills records not yet in historique.
                var histoRaw = await _db.PrieresJanazaHistorique.AsNoTracking()
                    .Where(p => p.DateCreation >= start && p.DateCreation < end)
                    .Select(p => new { p.NomDefunt, p.EstAnonyme, p.Genre, p.DateCreation, p.DeclarantPrenom, p.DeclarantNom })
                    .ToListAsync();

                var liveRaw = await _db.PrieresJanaza.AsNoTracking()
                    .Where(p => p.DateCreation >= start && p.DateCreation < end)
                    .Select(p => new { p.NomDefunt, p.EstAnonyme, p.Genre, p.DateCreation })
                    .ToListAsync();

                var histoDates = new HashSet<DateTime>(histoRaw.Select(r => r.DateCreation));

                DeclDetailDto ToDto(string? nom, bool anon, string? genre, DateTime utcDate, string? prenom, string? nomDecl) => new()
                {
                    nomDefunt        = nom,
                    estAnonyme       = anon,
                    genre            = genre,
                    heureDeclaration = ToLocal(utcDate).ToString("HH:mm"),
                    declarantPrenom  = prenom,
                    declarantNom     = nomDecl,
                };

                var result = histoRaw
                    .Where(r => InSlot(r.DateCreation))
                    .Select(r => ToDto(r.NomDefunt, r.EstAnonyme, r.Genre, r.DateCreation, r.DeclarantPrenom, r.DeclarantNom))
                    .Concat(liveRaw
                        .Where(r => !histoDates.Contains(r.DateCreation) && InSlot(r.DateCreation))
                        .Select(r => ToDto(r.NomDefunt, r.EstAnonyme, r.Genre, r.DateCreation, null, null)));

                return Ok(result);
            }
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats(
            [FromQuery] string  period           = "jour",
            [FromQuery] string? date             = null,
            [FromQuery] string? genre            = null,
            [FromQuery] string? pays             = null,
            [FromQuery] int?    mosqueeId        = null,
            [FromQuery] int     utcOffsetMinutes = 0)
        {
            if (!DateTime.TryParse(date, out var refDate))
                refDate = DateTime.UtcNow.AddMinutes(utcOffsetMinutes).Date;

            // localStart/localEnd = bornes dans le fuseau du client
            DateTime localStart, localEnd;
            switch (period)
            {
                case "semaine":
                    int dow = ((int)refDate.DayOfWeek + 6) % 7;
                    localStart = refDate.Date.AddDays(-dow);
                    localEnd   = localStart.AddDays(7);
                    break;
                case "mois":
                    localStart = new DateTime(refDate.Year, refDate.Month, 1);
                    localEnd   = localStart.AddMonths(1);
                    break;
                case "annee":
                    localStart = new DateTime(refDate.Year, 1, 1);
                    localEnd   = new DateTime(refDate.Year + 1, 1, 1);
                    break;
                default: // jour
                    localStart = refDate.Date;
                    localEnd   = localStart.AddDays(1);
                    break;
            }

            // Conversion en UTC pour les requêtes SQL (DateCreation stockée en UTC)
            DateTime start = localStart.AddMinutes(-utcOffsetMinutes);
            DateTime end   = localEnd.AddMinutes(-utcOffsetMinutes);

            // ── Declarations — live table is primary source (correct mosque name via FK join).
            // Historique fills in records purged from the live table.
            IQueryable<PriereJanaza> liveQuery = _db.PrieresJanaza.AsNoTracking()
                .Where(p => p.DateCreation >= start && p.DateCreation < end);
            IQueryable<PriereJanazaHistorique> histoQuery = _db.PrieresJanazaHistorique.AsNoTracking()
                .Where(p => p.DateCreation >= start && p.DateCreation < end);

            if (!string.IsNullOrEmpty(genre))
            {
                liveQuery  = liveQuery.Where(p => p.Genre == genre);
                histoQuery = histoQuery.Where(p => p.Genre == genre);
            }
            if (!string.IsNullOrEmpty(pays))
            {
                liveQuery  = liveQuery.Where(p => p.PaysEnterrement == pays);
                histoQuery = histoQuery.Where(p => p.Pays == pays);
            }

            var liveDecls = await liveQuery
                .Select(p => new { p.DateCreation, p.Genre, PaysEnterrement = p.PaysEnterrement, MosqueeNom = p.Mosquee != null ? p.Mosquee.Nom : null })
                .ToListAsync();

            var histoDecls = await histoQuery
                .Select(p => new { p.DateCreation, p.Genre, PaysEnterrement = p.Pays, MosqueeNom = p.MosqueeNom })
                .ToListAsync();

            // Prefer live entries; historique fills in only truly purged records
            var liveKeys   = new HashSet<DateTime>(liveDecls.Select(d => d.DateCreation));
            var declarations = liveDecls
                .Concat(histoDecls.Where(h => !liveKeys.Contains(h.DateCreation)))
                .ToList();

            // ── Users ─────────────────────────────────────────────────────────────
            var users = await _db.Utilisateurs.AsNoTracking()
                .Where(u => u.DateInscription >= start && u.DateInscription < end)
                .Select(u => u.DateInscription)
                .ToListAsync();

            // ── Global (all-time) user stats ─────────────────────────────────────
            var totalUtilisateurs = await _db.Utilisateurs.CountAsync();
            var platformCounts    = await _db.Utilisateurs.AsNoTracking()
                .GroupBy(u => u.Platform == null || u.Platform == "" ? "inconnu" : u.Platform.ToLower())
                .Select(g => new { platform = g.Key, count = g.Count() })
                .ToListAsync();
            int GetPlatformCount(string key) => platformCounts.FirstOrDefault(x => x.platform == key)?.count ?? 0;

            // ── Series builders ───────────────────────────────────────────────────
            var dayAbbr   = new[] { "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam", "Dim" };
            var monthAbbr = new[] { "Jan", "Fév", "Mar", "Avr", "Mai", "Juin", "Juil", "Aoû", "Sep", "Oct", "Nov", "Déc" };

            // Convert a UTC DateTime to local using the client's UTC offset
            DateTime ToLocal(DateTime utc) => utc.AddMinutes(utcOffsetMinutes);

            List<SeriesPointDto> BuildDeclSeries()
            {
                if (period == "jour")
                    return Enumerable.Range(0, 24)
                        .Select(h => new SeriesPointDto { label = $"{h}h", value = declarations.Count(d => ToLocal(d.DateCreation).Hour == h) })
                        .ToList();
                if (period == "semaine")
                    return Enumerable.Range(0, 7)
                        .Select(d => new SeriesPointDto { label = $"{dayAbbr[d]} {localStart.AddDays(d):dd/MM}", value = declarations.Count(x => ToLocal(x.DateCreation).Date == localStart.AddDays(d).Date) })
                        .ToList();
                if (period == "mois")
                {
                    int days = (int)(localEnd - localStart).TotalDays;
                    return Enumerable.Range(0, days)
                        .Select(d => new SeriesPointDto { label = localStart.AddDays(d).Day.ToString(), value = declarations.Count(x => ToLocal(x.DateCreation).Date == localStart.AddDays(d).Date) })
                        .ToList();
                }
                return Enumerable.Range(0, 12)
                    .Select(m => new SeriesPointDto { label = monthAbbr[m], value = declarations.Count(x => ToLocal(x.DateCreation).Month == m + 1) })
                    .ToList();
            }

            List<SeriesPointDto> BuildUserSeries()
            {
                if (period == "jour")
                    return Enumerable.Range(0, 24)
                        .Select(h => new SeriesPointDto { label = $"{h}h", value = users.Count(u => ToLocal(u).Hour == h) })
                        .ToList();
                if (period == "semaine")
                    return Enumerable.Range(0, 7)
                        .Select(d => new SeriesPointDto { label = $"{dayAbbr[d]} {localStart.AddDays(d):dd/MM}", value = users.Count(u => ToLocal(u).Date == localStart.AddDays(d).Date) })
                        .ToList();
                if (period == "mois")
                {
                    int days = (int)(localEnd - localStart).TotalDays;
                    return Enumerable.Range(0, days)
                        .Select(d => new SeriesPointDto { label = localStart.AddDays(d).Day.ToString(), value = users.Count(u => ToLocal(u).Date == localStart.AddDays(d).Date) })
                        .ToList();
                }
                return Enumerable.Range(0, 12)
                    .Select(m => new SeriesPointDto { label = monthAbbr[m], value = users.Count(u => ToLocal(u).Month == m + 1) })
                    .ToList();
            }

            return Ok(new
            {
                period,
                start,
                end,
                declarations = new
                {
                    total  = declarations.Count,
                    series = BuildDeclSeries(),
                    byGenre = new
                    {
                        homme  = declarations.Count(d => d.Genre == "homme"),
                        femme  = declarations.Count(d => d.Genre == "femme"),
                        enfant = declarations.Count(d => d.Genre == "enfant"),
                        inconnu = declarations.Count(d => d.Genre != "homme" && d.Genre != "femme" && d.Genre != "enfant")
                    },
                    byPays = declarations
                        .Where(d => !string.IsNullOrEmpty(d.PaysEnterrement))
                        .GroupBy(d => d.PaysEnterrement!)
                        .Select(g => new PaysCountDto { pays = g.Key, count = g.Count() })
                        .OrderByDescending(x => x.count)
                        .ToList(),
                    paysInconnu = declarations.Count(d => string.IsNullOrEmpty(d.PaysEnterrement)),
                    mosqueeInconnu = declarations.Count(d => string.IsNullOrEmpty(d.MosqueeNom)),
                    byMosquee = declarations
                        .Where(d => !string.IsNullOrEmpty(d.MosqueeNom))
                        .GroupBy(d => d.MosqueeNom!)
                        .Select(g => new MosqueeCountDto { nom = g.Key, count = g.Count() })
                        .OrderByDescending(x => x.count)
                        .ToList()
                },
                utilisateurs = new
                {
                    total  = users.Count,
                    series = BuildUserSeries()
                },
                globalStats = new
                {
                    totalUtilisateurs,
                    android = GetPlatformCount("android"),
                    ios     = GetPlatformCount("ios"),
                    inconnu = totalUtilisateurs - GetPlatformCount("android") - GetPlatformCount("ios"),
                }
            });
        }
    }
}
