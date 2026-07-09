using Microsoft.AspNetCore.Mvc;
using QabrWebApp.Builders;
using QabrWebApp.Domain.Models;
using QabrWebApp.Domain.Services;
using QabrWebApp.Request;
using QabrWebApp.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Collections.Concurrent;
using System.Text.Json;

namespace QabrWebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PriereJanazaController : ControllerBase
    {
        // One semaphore per (mosqueeId, utcMinuteBucket) prevents two simultaneous imports
        // for the same slot from both passing the conflict check before either writes.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _slotLocks = new();

        // One semaphore per normalized mosque name prevents two concurrent imports for the
        // same mosque from both finding it absent in DB and creating a duplicate.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _mosqueeCreationLocks = new();

        // French articles and prepositions — stay lowercase in mosque names (unless first word).
        private static readonly HashSet<string> _stopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "de", "du", "des", "le", "la", "les", "en", "et", "au", "aux",
            "sur", "sous", "par", "pour", "dans", "avec", "ou", "à", "a",
            "un", "une", "l", "d"
        };

        // Common nouns that appear frequently in mosque / venue names and stay lowercase.
        private static readonly HashSet<string> _commonNouns = new(StringComparer.OrdinalIgnoreCase)
        {
            "mosquée", "mosquee", "salle", "centre", "prière", "priere",
            "islamique", "association", "communauté", "communaute", "maison",
            "espace", "complexe", "grande", "chapelle", "hall", "al",
            "funérarium", "funerarium", "pompes", "funèbres", "funebres",
            "salon", "annexe", "petite", "nouvelle", "ancienne",
            "rue", "avenue", "boulevard", "allée", "allee", "place",
            "quartier", "cité", "cite"
        };

        private readonly IPriereJanazaService _service;
        private readonly IPriereJanazaViewModelBuilder _builder;
        private readonly IPushNotificationService _push;
        private readonly IMosqueeService _mosqueeService;
        private readonly IUtilisateurService _utilisateurService;
        private readonly IConfiguration _config;
        private readonly IImportSessionService _importSessions;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IEmailService _email;
        private readonly ITextImportSummaryService _textImportSummary;
        private readonly ITelegramNotificationBuilder _telegram;

        public PriereJanazaController(
            IPriereJanazaService service,
            IPriereJanazaViewModelBuilder builder,
            IPushNotificationService push,
            IMosqueeService mosqueeService,
            IUtilisateurService utilisateurService,
            IConfiguration config,
            IImportSessionService importSessions,
            IHttpClientFactory httpClientFactory,
            IEmailService email,
            ITextImportSummaryService textImportSummary,
            ITelegramNotificationBuilder telegram)
        {
            _service = service;
            _builder = builder;
            _push = push;
            _mosqueeService = mosqueeService;
            _utilisateurService = utilisateurService;
            _config = config;
            _importSessions = importSessions;
            _httpClientFactory = httpClientFactory;
            _email = email;
            _textImportSummary = textImportSummary;
            _telegram = telegram;
        }

        [HttpGet]
        [SwaggerOperation(Summary = "Liste toutes les prières")]
        public async Task<IActionResult> GetAll()
        {
            var list = await _service.GetAllAsync();
            return Ok(_builder.BuildList(list));
        }

        [HttpGet("upcoming")]
        [SwaggerOperation(Summary = "Prières à venir ou en cours")]
        public async Task<IActionResult> GetUpcoming()
        {
            var list = await _service.GetUpcomingAsync();
            return Ok(_builder.BuildList(list));
        }

        [HttpGet("{id}")]
        [SwaggerOperation(Summary = "Récupère une prière par ID")]
        public async Task<IActionResult> GetById(int id)
        {
            var p = await _service.GetByIdAsync(id);
            return p is null ? NotFound() : Ok(_builder.Build(p));
        }

        [HttpGet("mosquee/{mosqueeId}")]
        [SwaggerOperation(Summary = "Prières d'une mosquée")]
        public async Task<IActionResult> GetByMosquee(int mosqueeId)
        {
            var list = await _service.GetByMosqueeIdAsync(mosqueeId);
            return Ok(_builder.BuildList(list));
        }

        [HttpGet("utilisateur/{utilisateurId}")]
        [SwaggerOperation(Summary = "Déclarations d'un utilisateur")]
        public async Task<IActionResult> GetByUtilisateur(int utilisateurId)
        {
            var list = await _service.GetByUtilisateurIdAsync(utilisateurId);
            return Ok(_builder.BuildList(list));
        }

        [HttpGet("en-attente")]
        [SwaggerOperation(Summary = "Prières en attente de validation de lieu")]
        public async Task<IActionResult> GetPending()
        {
            var list = await _service.GetPendingAsync();
            return Ok(_builder.BuildList(list));
        }

        [HttpPost]
        [SwaggerOperation(Summary = "Déclare une prière funéraire et notifie les abonnés")]
        public async Task<IActionResult> Create([FromBody] PriereJanazaRequest req)
        {
            var mosquee = await _mosqueeService.GetByIdAsync(req.MosqueeId);
            var mosqueeEnAttente = mosquee?.Statut == "EnAttente";

            // Normalisation du nom avant insertion
            var nomDefunt = (req.EstAnonyme == true) ? null : NormalizeName(req.NomDefunt);

            // Vérification doublon : même défunt + même jour + même mosquée
            if (nomDefunt is not null && req.MosqueeId > 0)
            {
                var existingPrieres = await _service.GetByMosqueeIdAsync(req.MosqueeId);
                if (FindNameDayConflict(existingPrieres, nomDefunt, req.DateHeurePriere) is not null)
                    return Conflict(new { error = "Cette janaza pour cette personne existe déjà." });
            }

            var priere = new PriereJanaza
            {
                MosqueeId = req.MosqueeId,
                UtilisateurId = req.UtilisateurId,
                NomDefunt = nomDefunt,
                EstAnonyme = req.EstAnonyme,
                Genre = req.Genre,
                DateHeurePriere = req.DateHeurePriere,
                Commentaire = req.Commentaire,
                PaysEnterrement = req.PaysEnterrement,
                VilleEnterrement = req.VilleEnterrement,
                AnneeNaissance = req.AnneeNaissance,
                AnneeDeces = req.AnneeDeces,
                UtcOffsetMinutes = req.UtcOffsetMinutes,
                Statut = mosqueeEnAttente ? StatutPriere.EnAttente : StatutPriere.AVenir,
            };

            var created = await _service.CreateAsync(priere);

            // Récupération de l'utilisateur pour la notification Telegram (une seule requête DB)
            Utilisateur? utilisateur = null;
            if (req.UtilisateurId is int uid && uid > 0)
                utilisateur = await _utilisateurService.GetByIdAsync(uid);

            if (!mosqueeEnAttente)
            {
                await _push.NotifyMosqueeSubscribersAsync(req.MosqueeId, created);
                await _push.ScheduleMosqueeReminderAsync(req.MosqueeId, created);
                _ = _telegram.NotifyNewJanazaAsync(created, mosquee?.Nom ?? "—", mosquee?.Adresse, utilisateur);
            }
            else
            {
                var supportEmail = _config["EmailSettings:RecipientEmail"] ?? "support@salatjanaza.org";
                var defunt = (req.EstAnonyme == true || string.IsNullOrWhiteSpace(req.NomDefunt))
                    ? "Défunt(e) anonyme"
                    : req.NomDefunt;
                var dateStr = req.DateHeurePriere.ToString("dd/MM/yyyy à HH:mm", System.Globalization.CultureInfo.InvariantCulture);

                var html = new System.Text.StringBuilder();
                html.AppendLine("<h2>Nouvelle déclaration de janaza en attente</h2>");
                html.AppendLine("<p>Une janaza a été déclarée pour un lieu qui n'a pas encore été validé.</p>");
                html.AppendLine("<hr/>");
                html.AppendLine("<h3>Lieu (en attente de validation)</h3>");
                html.AppendLine($"<p><strong>Nom :</strong> {mosquee?.Nom ?? "—"}</p>");
                html.AppendLine($"<p><strong>Adresse :</strong> {mosquee?.Adresse ?? "—"}</p>");
                html.AppendLine($"<p><strong>ID lieu :</strong> {req.MosqueeId}</p>");
                html.AppendLine("<h3>Janaza déclarée</h3>");
                html.AppendLine($"<p><strong>Défunt(e) :</strong> {defunt}</p>");
                html.AppendLine($"<p><strong>Date de la prière :</strong> {dateStr} UTC</p>");
                html.AppendLine($"<p><strong>ID janaza :</strong> {created.Id}</p>");
                html.AppendLine("<hr/>");
                html.AppendLine("<p>Connectez-vous à l'interface admin → Déclarations → En attente pour valider ou refuser ce lieu. La janaza sera publiée automatiquement dès la validation.</p>");

                _ = _email.SendNotificationAsync(supportEmail,
                    $"[Salat Janaza] Déclaration en attente – lieu à valider : {mosquee?.Nom ?? $"ID {req.MosqueeId}"}",
                    html.ToString());
                _ = _telegram.NotifyPendingJanazaAsync(created, mosquee?.Nom ?? "—");
            }

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, _builder.Build(created));
        }

        [HttpPut("{id}")]
        [SwaggerOperation(Summary = "Met à jour une prière")]
        public async Task<IActionResult> Update(int id, [FromBody] PriereJanazaRequest req)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();
            existing.MosqueeId = req.MosqueeId;
            existing.NomDefunt = req.NomDefunt;
            existing.EstAnonyme = req.EstAnonyme;
            existing.Genre = req.Genre;
            existing.DateHeurePriere = req.DateHeurePriere;
            existing.Commentaire = req.Commentaire;
            existing.PaysEnterrement = req.PaysEnterrement;
            existing.VilleEnterrement = req.VilleEnterrement;
            existing.AnneeNaissance = req.AnneeNaissance;
            existing.AnneeDeces = req.AnneeDeces;
            existing.UtcOffsetMinutes = req.UtcOffsetMinutes;
            var updated = await _service.UpdateAsync(existing);
            // Annule l'ancien rappel et en planifie un nouveau avec la nouvelle heure
            await _push.RescheduleMosqueeReminderAsync(req.MosqueeId, updated);
            return Ok(_builder.Build(updated));
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(Summary = "Supprime une prière")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var existing = await _service.GetByIdAsync(id);
                if (existing is null) return NotFound();
                await _service.DeleteAsync(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, inner = ex.InnerException?.Message, type = ex.GetType().Name });
            }
        }

        [HttpPost("import/text")]
        [SwaggerOperation(Summary = "Import en masse depuis un fichier texte (même token réutilisable, clé API requise)")]
        public async Task<IActionResult> ImportText([FromBody] PriereJanazaTextImportRequest req)
        {
            var providedKey = Request.Headers["X-Import-Key"].FirstOrDefault();
            var expectedKey = _config["ImportApiKey"];
            if (string.IsNullOrEmpty(expectedKey) || providedKey != expectedKey)
                return Unauthorized(new { error = "Clé API invalide." });

            // Pour l'import texte : chercher d'abord par nom dans toute la base (pas seulement
            // à proximité GPS) pour éviter que la recherche de proximité retourne une mosquée
            // différente à cause du fallback géographique.
            var (mosquee, geocodingFailed, countryCode) = await ResolveOrCreateMosqueeForTextImportAsync(req.MosqueeNom, req.MosqueeAdresse);

            if (mosquee is null)
            {
                var skipReason = geocodingFailed ? "GEOCODING_FAILED" : "MOSQUE_NOT_FOUND";
                _textImportSummary.AddSkip(req.TextImportToken, req.MosqueeNom, req.EstAnonyme ? null : req.NomDefunt, req.DateHeurePriere, skipReason);
                return Ok(new
                {
                    skipped = true,
                    reason = skipReason,
                    mosqueeNom = req.MosqueeNom,
                    nomDefunt = req.EstAnonyme ? null : req.NomDefunt,
                    dateHeurePriere = req.DateHeurePriere,
                });
            }

            var tz = GetTimezoneForCountry(countryCode);
            var utcOffset = tz.GetUtcOffset(req.DateHeurePriere);
            var utcDate = DateTime.SpecifyKind(req.DateHeurePriere, DateTimeKind.Utc);
            var utcOffsetMinutes = (int)utcOffset.TotalMinutes;

            var lockKey = $"{mosquee.Id}_{utcDate:yyyyMMddHHmm}";
            var slotLock = _slotLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await slotLock.WaitAsync();
            bool lockReleased = false;
            PriereJanaza created;
            try
            {
                var existing = await _service.GetByMosqueeIdAsync(mosquee.Id);
                var conflict = existing.FirstOrDefault(p =>
                {
                    var pd = DateTime.SpecifyKind(p.DateHeurePriere, DateTimeKind.Utc);
                    return pd.Year == utcDate.Year && pd.Month == utcDate.Month
                        && pd.Day == utcDate.Day && pd.Hour == utcDate.Hour
                        && pd.Minute == utcDate.Minute;
                });

                if (conflict is not null)
                {
                    var conflictLabel = conflict.EstAnonyme ? "anonyme" : (conflict.NomDefunt ?? "inconnu(e)");
                    slotLock.Release();
                    lockReleased = true;
                    _textImportSummary.AddSkip(req.TextImportToken, mosquee.Nom, req.EstAnonyme ? null : req.NomDefunt, req.DateHeurePriere, $"SLOT_CONFLICT ({conflictLabel})");
                    return Ok(new
                    {
                        skipped = true,
                        reason = $"SLOT_CONFLICT ({conflictLabel})",
                        mosqueeNom = mosquee.Nom,
                        nomDefunt = req.EstAnonyme ? null : req.NomDefunt,
                        dateHeurePriere = req.DateHeurePriere,
                    });
                }

                // Normalisation du nom et vérification doublon nom+jour
                var nomDefunt = req.EstAnonyme ? null : NormalizeName(req.NomDefunt);
                if (nomDefunt is not null)
                {
                    var nameConflict = FindNameDayConflict(existing, nomDefunt, utcDate);
                    if (nameConflict is not null)
                    {
                        slotLock.Release();
                        lockReleased = true;
                        _textImportSummary.AddSkip(req.TextImportToken, mosquee.Nom, nomDefunt, req.DateHeurePriere, "DUPLICATE_PERSON");
                        return Ok(new
                        {
                            skipped = true,
                            reason = "DUPLICATE_PERSON",
                            mosqueeNom = mosquee.Nom,
                            nomDefunt,
                            dateHeurePriere = req.DateHeurePriere,
                        });
                    }
                }

                var priere = new PriereJanaza
                {
                    MosqueeId = mosquee.Id,
                    UtilisateurId = req.UtilisateurId,
                    NomDefunt = nomDefunt,
                    EstAnonyme = req.EstAnonyme,
                    Genre = req.Genre?.ToLowerInvariant(),
                    DateHeurePriere = utcDate,
                    Commentaire = req.Commentaire,
                    PaysEnterrement = req.PaysEnterrement,
                    VilleEnterrement = req.VilleEnterrement,
                    AnneeNaissance = req.AnneeNaissance,
                    AnneeDeces = req.AnneeDeces,
                    UtcOffsetMinutes = utcOffsetMinutes,
                };

                created = await _service.CreateAsync(priere);
            }
            finally
            {
                if (!lockReleased) slotLock.Release();
            }

            _textImportSummary.AddSuccess(req.TextImportToken, mosquee.Nom);

            await _push.NotifyMosqueeSubscribersAsync(mosquee.Id, created);
            await _push.ScheduleMosqueeReminderAsync(mosquee.Id, created);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, new
            {
                skipped = false,
                priereId = created.Id,
                mosqueeId = mosquee.Id,
                mosqueeNom = mosquee.Nom,
                mosqueeAdresse = mosquee.Adresse,
                textImportToken = req.TextImportToken,
            });
        }

        [HttpPost("import")]
        [SwaggerOperation(Summary = "Import d'une prière depuis un agent IA (clé API requise)")]
        public async Task<IActionResult> Import([FromBody] PriereJanazaImportRequest req)
        {
            var providedKey = Request.Headers["X-Import-Key"].FirstOrDefault();
            var expectedKey = _config["ImportApiKey"];
            if (string.IsNullOrEmpty(expectedKey) || providedKey != expectedKey)
                return Unauthorized(new { error = "Clé API invalide." });

            var session = _importSessions.Get(req.ImportToken);
            if (session is null)
                return BadRequest(new { error = "Token d'import invalide ou expiré." });

            // Résolution de la mosquée par GPS
            var (mosquee, geocodingFailed, countryCode) = await ResolveOrCreateMosqueeAsync(req.MosqueeNom, req.MosqueeAdresse);
            if (mosquee is null)
            {
                if (geocodingFailed)
                    _importSessions.SetError(req.ImportToken, "L'adresse de la mosquée est illisible sur l'image.", errorCode: "IMAGE_QUALITY");
                else
                    _importSessions.SetError(req.ImportToken, $"Mosquée introuvable : \"{req.MosqueeNom}\".");
                return BadRequest(new { error = geocodingFailed ? "IMAGE_QUALITY" : $"Impossible de trouver ou créer la mosquée \"{req.MosqueeNom}\"." });
            }

            // L'heure extraite du flyer est l'heure locale de la mosquée (wall-clock).
            // On la stocke telle quelle en UTC faussé — affichage = même chiffre partout.
            // utcOffsetMinutes est conservé pour les notifications (30min avant heure locale).
            var tz = GetTimezoneForCountry(countryCode);
            var utcOffset = tz.GetUtcOffset(req.DateHeurePriere);
            var utcDate = DateTime.SpecifyKind(req.DateHeurePriere, DateTimeKind.Utc);
            var utcOffsetMinutes = (int)utcOffset.TotalMinutes;

            // Slot lock: prevents two concurrent imports for the same mosque+hour
            var lockKey = $"{mosquee.Id}_{utcDate:yyyyMMddHH}";
            var slotLock = _slotLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await slotLock.WaitAsync();
            PriereJanaza created;
            try
            {
                // Vérification créneau occupé : une seule janaza par mosquée par heure (en UTC)
                var existing = await _service.GetByMosqueeIdAsync(mosquee.Id);
                var conflict = existing.FirstOrDefault(p =>
                {
                    var pd = DateTime.SpecifyKind(p.DateHeurePriere, DateTimeKind.Utc);
                    return pd.Year == utcDate.Year && pd.Month == utcDate.Month
                        && pd.Day == utcDate.Day && pd.Hour == utcDate.Hour;
                });

                if (conflict is not null)
                {
                    var conflictLabel = conflict.EstAnonyme
                        ? "anonyme"
                        : (conflict.NomDefunt ?? "inconnu(e)");
                    var conflictTime = DateTime.SpecifyKind(conflict.DateHeurePriere, DateTimeKind.Utc)
                        .ToString("dd/MM/yyyy à HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                    var msg = $"Une janaza est déjà programmée dans cette mosquée le {conflictTime} UTC ({conflictLabel}). Une seule janaza par créneau horaire est autorisée.";
                    _importSessions.SetError(req.ImportToken, msg);
                    return Conflict(new { error = msg });
                }

                // Normalisation du nom et vérification doublon nom+jour
                var nomDefunt = req.EstAnonyme ? null : NormalizeName(req.NomDefunt);
                if (nomDefunt is not null)
                {
                    var nameConflict = FindNameDayConflict(existing, nomDefunt, utcDate);
                    if (nameConflict is not null)
                    {
                        var msg = "Cette janaza pour cette personne existe déjà.";
                        _importSessions.SetError(req.ImportToken, msg);
                        return Conflict(new { error = msg });
                    }
                }

                var priere = new PriereJanaza
                {
                    MosqueeId = mosquee.Id,
                    UtilisateurId = session.UtilisateurId,
                    NomDefunt = nomDefunt,
                    EstAnonyme = req.EstAnonyme,
                    Genre = req.Genre?.ToLowerInvariant(),
                    DateHeurePriere = utcDate,
                    Commentaire = req.Commentaire,
                    PaysEnterrement = req.PaysEnterrement,
                    VilleEnterrement = req.VilleEnterrement,
                    AnneeNaissance = req.AnneeNaissance,
                    AnneeDeces = req.AnneeDeces,
                    UtcOffsetMinutes = utcOffsetMinutes,
                };

                created = await _service.CreateAsync(priere);
            }
            finally
            {
                slotLock.Release();
            }
            var timeUnknown = req.DateHeurePriere.Hour == 0 && req.DateHeurePriere.Minute == 0;
            _importSessions.SetSuccess(req.ImportToken, created.Id, timeUnknown);

            await _push.NotifyMosqueeSubscribersAsync(mosquee.Id, created);
            await _push.ScheduleMosqueeReminderAsync(mosquee.Id, created);

            if (!string.IsNullOrEmpty(session.ExpoPushToken))
            {
                await _push.SendToTokenAsync(
                    session.ExpoPushToken,
                    "Janaza importée ✓",
                    $"La janaza de {(req.EstAnonyme ? "défunt(e) anonyme" : req.NomDefunt)} a été ajoutée avec succès.");
            }

            // Notification Telegram : nouvelle janaza via import flyer
            Utilisateur? utilisateurFlyer = null;
            if (session.UtilisateurId > 0)
                utilisateurFlyer = await _utilisateurService.GetByIdAsync(session.UtilisateurId);
            _ = _telegram.NotifyNewJanazaAsync(created, mosquee.Nom, mosquee.Adresse, utilisateurFlyer);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, _builder.Build(created));
        }

        private async Task<(Mosquee? mosquee, bool geocodingFailed, string? countryCode)> ResolveOrCreateMosqueeAsync(string nom, string? adresse)
        {
            // 1. Geocoder via Nominatim.
            // L'adresse seule est testée EN PREMIER : inclure le nom de la mosquée dans la requête
            // peut tromper Nominatim qui retourne alors un POI mal indexé dans OSM
            // (ex : "MOSQUEE DE MEAUX, 20 AV. HENRI DUNANT" → Nominatim retourne une mosquée
            // homonyme dans une autre ville, causant une recherche de proximité au mauvais endroit).
            double? lat = null, lng = null;
            string? countryCode = null;
            var city = ExtractCity(adresse);

            var geocodeQueries = new List<string>();
            if (!string.IsNullOrWhiteSpace(adresse)) geocodeQueries.Add(adresse);           // 1. adresse seule  (plus fiable)
            if (city != null)                        geocodeQueries.Add($"{nom}, {city}");   // 2. nom + ville
            if (!string.IsNullOrWhiteSpace(adresse)) geocodeQueries.Add($"{nom}, {adresse}"); // 3. nom + adresse (dernier recours)

            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Add("User-Agent", "QabrApp/1.0");

            foreach (var q in geocodeQueries)
            {
                try
                {
                    var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(q)}&format=json&limit=1&addressdetails=1";
                    var resp = await http.GetAsync(url);
                    if (!resp.IsSuccessStatusCode) continue;

                    var json = await resp.Content.ReadAsStringAsync();
                    var results = JsonSerializer.Deserialize<List<NominatimResult>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (results is { Count: > 0 })
                    {
                        lat = double.Parse(results[0].Lat, System.Globalization.CultureInfo.InvariantCulture);
                        lng = double.Parse(results[0].Lon, System.Globalization.CultureInfo.InvariantCulture);
                        countryCode = results[0].Address?.CountryCode;
                        break;
                    }
                }
                catch { }
            }

            // Géocodage impossible : adresse illisible sur l'image
            if (!lat.HasValue || !lng.HasValue)
                return (null, geocodingFailed: true, null);

            // 2. Chercher une mosquée existante dans un rayon de 500m EN PRIORITISANT LE NOM.
            // Sans validation du nom, une mosquée homonyme ou proche mais différente
            // (ex : "Mosquée Arrahma" alors qu'on importe pour "Mosquée de Meaux") serait retournée.
            var nearby = await _mosqueeService.GetNearbyAsync(lat.Value, lng.Value, 0.50);
            if (nearby.Count > 0)
            {
                // Priorité 1 : mosquée proche dont le nom partage au moins un mot significatif
                var nameMatch = nearby
                    .Where(m => MosqueeNamesCompatible(nom, m.Nom))
                    .OrderBy(m => Haversine(lat.Value, lng.Value, m.Latitude, m.Longitude))
                    .FirstOrDefault();
                if (nameMatch != null)
                    return (nameMatch, false, countryCode);

                // Priorité 2 : mosquée très proche (< 80 m) sans contrainte de nom
                // (même bâtiment, noms orthographiés différemment par des acteurs différents)
                var veryClose = nearby
                    .Where(m => Haversine(lat.Value, lng.Value, m.Latitude, m.Longitude) < 0.08)
                    .OrderBy(m => Haversine(lat.Value, lng.Value, m.Latitude, m.Longitude))
                    .FirstOrDefault();
                if (veryClose != null)
                    return (veryClose, false, countryCode);

                // Aucune correspondance satisfaisante → créer une nouvelle mosquée
            }

            // 3. Créer la mosquée si introuvable
            var created = await _mosqueeService.CreateAsync(new Mosquee
            {
                Nom = FormatMosqueeName(nom),
                Adresse = adresse,
                Latitude = lat.Value,
                Longitude = lng.Value,
                Statut = "Validee",
                Source = "import",
            });
            return (created, false, countryCode);
        }

        // Résolution pour l'import texte uniquement : NE TOUCHE PAS à ResolveOrCreateMosqueeAsync.
        // Algorithme : 1) cherche par nom dans toute la base, 2) si absent → géocode + crée.
        // Pas de recherche par proximité GPS pour éviter les faux positifs (mosquées voisines).
        private async Task<(Mosquee? mosquee, bool geocodingFailed, string? countryCode)> ResolveOrCreateMosqueeForTextImportAsync(string nom, string? adresse)
        {
            // Sérialiser les imports concurrents pour le même nom de mosquée :
            // sans ce verrou, deux imports simultanés pour "Mosquée Salam" trouveraient
            // tous les deux la mosquée absente et créeraient chacun un doublon.
            var creationLockKey = FormatMosqueeName(nom).ToLowerInvariant();
            var creationLock = _mosqueeCreationLocks.GetOrAdd(creationLockKey, _ => new SemaphoreSlim(1, 1));
            await creationLock.WaitAsync();
            try
            {
            return await ResolveOrCreateMosqueeForTextImportCoreAsync(nom, adresse);
            }
            finally
            {
                creationLock.Release();
            }
        }

        private async Task<(Mosquee? mosquee, bool geocodingFailed, string? countryCode)> ResolveOrCreateMosqueeForTextImportCoreAsync(string nom, string? adresse)
        {
            // 1. Recherche par nom dans toute la base de données
            var nameResults = await _mosqueeService.SearchAsync(nom);
            var nameMatches = nameResults.Where(m => MosqueeNamesCompatible(nom, m.Nom)).ToList();

            if (nameMatches.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(adresse))
                {
                    // Règle : si une mosquée existante a le même nom ET la même adresse → c'est elle.
                    // Si aucune adresse en base ne correspond → c'est une nouvelle mosquée, on la crée.
                    // (Même s'il n'y a qu'un seul candidat de même nom : adresse différente = nouvelle mosquée.)
                    var addressMatch = nameMatches.FirstOrDefault(m => AddressesMatch(m.Adresse, adresse));
                    if (addressMatch != null)
                        return (addressMatch, false, null);
                    // Aucune adresse ne correspond → fall through vers ForceCreate
                }
                else
                {
                    // Pas d'adresse fournie : impossible de vérifier → retourner le premier candidat (best-effort)
                    return (nameMatches[0], false, null);
                }
            }

            // 2. Géocoder pour obtenir les coordonnées (nouvelle mosquée ou candidat indéterminable)
            double? lat = null, lng = null;
            string? countryCode = null;
            var city = ExtractCity(adresse);

            var geocodeQueries = new List<string>();
            if (!string.IsNullOrWhiteSpace(adresse)) geocodeQueries.Add(adresse);
            if (city != null)                        geocodeQueries.Add($"{nom}, {city}");
            if (!string.IsNullOrWhiteSpace(adresse)) geocodeQueries.Add($"{nom}, {adresse}");

            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "QabrApp/1.0");

            foreach (var q in geocodeQueries)
            {
                try
                {
                    var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(q)}&format=json&limit=1&addressdetails=1";
                    var resp = await http.GetAsync(url);
                    if (!resp.IsSuccessStatusCode) continue;
                    var json = await resp.Content.ReadAsStringAsync();
                    var results = JsonSerializer.Deserialize<List<NominatimResult>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (results is { Count: > 0 })
                    {
                        lat = double.Parse(results[0].Lat, System.Globalization.CultureInfo.InvariantCulture);
                        lng = double.Parse(results[0].Lon, System.Globalization.CultureInfo.InvariantCulture);
                        countryCode = results[0].Address?.CountryCode;
                        break;
                    }
                }
                catch { }
            }

            if (!lat.HasValue || !lng.HasValue)
                return (null, geocodingFailed: true, null);

            // 3a. Dédup avant création : recherche une mosquée existante dans un rayon de 200m.
            // Critère 1 : même adresse exacte (CP + n° de rue + mots-clés).
            // Critère 2 (fallback) : coordonnées GPS strictement identiques — fiable quand
            //   l'adresse est de type "code postal + ville" sans numéro de rue, car Nominatim
            //   retourne toujours les mêmes coordonnées pour la même chaîne d'adresse.
            var nearbyForDedup = await _mosqueeService.GetNearbyAsync(lat.Value, lng.Value, 0.2);
            var dedupMatch = nearbyForDedup.FirstOrDefault(m => SameExactAddress(m.Adresse, adresse))
                          ?? nearbyForDedup.FirstOrDefault(m =>
                                m.Latitude == lat.Value && m.Longitude == lng.Value);
            if (dedupMatch is not null)
                return (dedupMatch, false, countryCode);

            // 3b. Créer la mosquée directement — ForceCreateAsync ignore la vérification de proximité
            // (CreateAsync a une tolérance de 110m qui retournerait une mosquée voisine incorrecte)
            var created = await _mosqueeService.ForceCreateAsync(new Mosquee
            {
                Nom = FormatMosqueeName(nom),
                Adresse = adresse,
                Latitude = lat.Value,
                Longitude = lng.Value,
                Source = "import",
            });
            return (created, false, countryCode);
        }

        // ── Normalisation du nom du défunt ────────────────────────────────────────
        // Première lettre de chaque mot en majuscule, reste en minuscule (Title Case).
        // Appliqué avant toute insertion pour garantir une casse cohérente en base.
        private static string? NormalizeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo
                .ToTitleCase(name.Trim().ToLowerInvariant());
        }

        // ── Vérification de doublon de prière ────────────────────────────────────
        // Retourne la prière existante si même nom (insensible à la casse) + même jour + même mosquée.
        // Utilisé dans les trois chemins d'insertion (Create, ImportText, Import).
        private static PriereJanaza? FindNameDayConflict(
            IEnumerable<PriereJanaza> existing,
            string? nomDefunt,
            DateTime prayerDay)
        {
            if (string.IsNullOrWhiteSpace(nomDefunt)) return null;
            return existing.FirstOrDefault(p =>
                !p.EstAnonyme &&
                p.DateHeurePriere.Date == prayerDay.Date &&
                string.Equals(p.NomDefunt?.Trim(), nomDefunt.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // ── Correspondance exacte d'adresse (pour dédup mosquée TXT import) ─────
        // Retourne vrai si les deux adresses représentent le même lieu physique :
        // code postal identique + numéro de rue identique + ≥2 mots significatifs communs.
        private static bool SameExactAddress(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

            var pcA = ExtractPostalCode(a);
            var pcB = ExtractPostalCode(b);
            if (pcA != null && pcB != null && pcA != pcB) return false;

            var snA = ExtractStreetNumber(a);
            var snB = ExtractStreetNumber(b);
            if (snA != null && snB != null && snA != snB) return false;

            // Au moins un ancrage (CP ou numéro) doit correspondre pour éviter les faux positifs
            bool hasAnchor = (pcA != null && pcB != null) || (snA != null && snB != null);
            if (!hasAnchor) return false;

            var wordsA = ExtractAddressKeywords(a);
            var wordsB = ExtractAddressKeywords(b);
            return wordsA.Count(w => wordsB.Contains(w)) >= 2;
        }

        private static string? ExtractPostalCode(string s)
        {
            var m = System.Text.RegularExpressions.Regex.Match(s, @"\b(\d{4,6})\b");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string? ExtractStreetNumber(string s)
        {
            var m = System.Text.RegularExpressions.Regex.Match(s.TrimStart(), @"^(\d+)\b");
            if (!m.Success) return null;
            var candidate = m.Groups[1].Value;
            // Ignorer si c'est le même token que le code postal (ex : "26100 Romans-sur-Isère")
            var pc = ExtractPostalCode(s);
            return candidate == pc ? null : candidate;
        }

        // Retourne vrai si les deux adresses partagent au moins 2 mots-clés significatifs
        // (nom de rue, ville, numéro...). Tolère les variantes d'écriture mineures.
        private static bool AddressesMatch(string? dbAddress, string? inputAddress)
        {
            if (string.IsNullOrWhiteSpace(dbAddress) || string.IsNullOrWhiteSpace(inputAddress))
                return false;
            var wordsA = ExtractAddressKeywords(dbAddress);
            var wordsB = ExtractAddressKeywords(inputAddress);
            return wordsA.Count(w => wordsB.Contains(w)) >= 2;
        }

        private static readonly HashSet<string> _addressStopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "rue", "avenue", "boulevard", "allee", "place", "impasse", "chemin",
            "route", "voie", "bis", "ter", "les", "des", "del"
        };

        private static HashSet<string> ExtractAddressKeywords(string address)
        {
            static char StripAccent(char c) => c switch
            {
                'é' or 'è' or 'ê' or 'ë' => 'e',
                'à' or 'â' or 'ä'         => 'a',
                'ô' or 'ö'                => 'o',
                'î' or 'ï'                => 'i',
                'ù' or 'û' or 'ü'         => 'u',
                'ç'                       => 'c',
                _                         => c,
            };

            var normalized = new string(address.ToLowerInvariant().Select(StripAccent).ToArray());
            return normalized
                .Split(new[] { ' ', ',', '.', '(', ')', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3 && !_addressStopWords.Contains(w))
                .ToHashSet();
        }

        // Retourne vrai si les deux noms de mosquée partagent au moins un mot significatif
        // (hors mots outils et noms génériques comme "mosquée", "salle", "centre"…).
        private static bool MosqueeNamesCompatible(string a, string b)
        {
            static HashSet<string> Significant(string name) =>
                name.ToLowerInvariant()
                    .Split(new[] { ' ', '-', '\'', '’', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => !_stopWords.Contains(w) && !_commonNouns.Contains(w) && w.Length > 1)
                    .ToHashSet();

            var sigA = Significant(a);
            var sigB = Significant(b);

            // Si l'un des deux noms n'a aucun mot significatif, on ne peut pas discriminer → accepter
            if (sigA.Count == 0 || sigB.Count == 0) return true;

            return sigA.Any(w => sigB.Contains(w));
        }

        private static TimeZoneInfo GetTimezoneForCountry(string? countryCode)
        {
            var ianaId = countryCode?.ToLowerInvariant() switch
            {
                "fr" or "be" or "lu" or "nl" or "es" or "it" or "de" or "ch" or "at" or "pl"
                or "no" or "se" or "dk" or "mc" or "ad" or "li" or "hu" or "cz" or "sk"
                or "si" or "hr" or "ba" or "rs" or "me" or "mk" or "al" => "Europe/Paris",
                "gb" or "ie"       => "Europe/London",
                "pt"               => "Europe/Lisbon",
                "ma"               => "Africa/Casablanca",
                "tn"               => "Africa/Tunis",
                "dz"               => "Africa/Algiers",
                "ly"               => "Africa/Tripoli",
                "eg"               => "Africa/Cairo",
                "tr"               => "Europe/Istanbul",
                "sa" or "kw" or "bh" or "qa" or "ye" or "jo" or "iq" => "Asia/Riyadh",
                "ae" or "om"       => "Asia/Dubai",
                "pk"               => "Asia/Karachi",
                "bd"               => "Asia/Dhaka",
                "sn" or "gn" or "ml" or "mr" or "bf" or "ne" or "tg" or "bj" or "gh"
                or "sl" or "lr" or "gw" or "cv" or "gm" => "Africa/Abidjan",
                "ci" or "ng" or "cm" or "ga" or "cg" or "cd" => "Africa/Lagos",
                "so" or "sd" or "km" or "dj" or "er" or "et" => "Africa/Nairobi",
                "cn"               => "Asia/Shanghai",
                "id"               => "Asia/Jakarta",
                "my" or "bn"       => "Asia/Kuala_Lumpur",
                "ph"               => "Asia/Manila",
                "af"               => "Asia/Kabul",
                "ir"               => "Asia/Tehran",
                "uz" or "tj" or "tm" => "Asia/Tashkent",
                "kz"               => "Asia/Almaty",
                "az"               => "Asia/Baku",
                _                  => "UTC"
            };

            // .NET 8 supporte les IANA IDs nativement sur Windows/Linux.
            // Si l'environnement ne le supporte pas, on tombe en UTC.
            if (TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out var tz)) return tz;

            // Fallback Windows timezone IDs (Windows Server sans ICU)
            var winId = ianaId switch
            {
                "Europe/Paris"    => "Romance Standard Time",
                "Europe/London"   => "GMT Standard Time",
                "Europe/Lisbon"   => "GMT Standard Time",
                "Africa/Casablanca" => "Morocco Standard Time",
                "Africa/Tunis"    => "W. Central Africa Standard Time",
                "Africa/Algiers"  => "W. Central Africa Standard Time",
                "Africa/Tripoli"  => "Libya Standard Time",
                "Africa/Cairo"    => "Egypt Standard Time",
                "Europe/Istanbul" => "Turkey Standard Time",
                "Asia/Riyadh"     => "Arab Standard Time",
                "Asia/Dubai"      => "Arabian Standard Time",
                "Asia/Karachi"    => "Pakistan Standard Time",
                "Asia/Dhaka"      => "Bangladesh Standard Time",
                "Africa/Abidjan"  => "Greenwich Standard Time",
                "Africa/Lagos"    => "W. Central Africa Standard Time",
                "Africa/Nairobi"  => "E. Africa Standard Time",
                "Asia/Shanghai"   => "China Standard Time",
                "Asia/Jakarta"    => "SE Asia Standard Time",
                "Asia/Kuala_Lumpur" => "Singapore Standard Time",
                "Asia/Manila"     => "Singapore Standard Time",
                "Asia/Kabul"      => "Afghanistan Standard Time",
                "Asia/Tehran"     => "Iran Standard Time",
                "Asia/Tashkent"   => "West Asia Standard Time",
                "Asia/Almaty"     => "Central Asia Standard Time",
                "Asia/Baku"       => "Azerbaijan Standard Time",
                _                 => "UTC"
            };

            return TimeZoneInfo.TryFindSystemTimeZoneById(winId, out tz) ? tz : TimeZoneInfo.Utc;
        }

        private static string? ExtractCity(string? adresse)
        {
            if (string.IsNullOrWhiteSpace(adresse)) return null;
            var parts = adresse.Split(',');
            if (parts.Length < 2) return null;
            var last = parts[^1].Trim();
            var m = System.Text.RegularExpressions.Regex.Match(last, @"^\d{4,5}\s+(.+)$");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static double Haversine(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        // Formats a mosque name to smart French title case:
        // - First word always capitalized
        // - Stop words and common venue nouns stay lowercase
        // - Contracted articles before apostrophes (d', l') stay lowercase;
        //   the word following the apostrophe is capitalized (it's a proper noun / city)
        // - Everything else (person names, city names) is capitalized
        // Examples: "FUNERARIUM D'ORLY" → "Funerarium d'Orly"
        //           "salle de prière bilal" → "Salle de prière Bilal"
        private static string FormatMosqueeName(string? nom)
        {
            if (string.IsNullOrWhiteSpace(nom)) return nom ?? "";

            var tokens = nom.Trim().ToLowerInvariant()
                            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < tokens.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                var token = tokens[i];

                // Handle apostrophe: "d'orly" → "d'Orly", "l'union" → "l'Union"
                var apos = token.IndexOf('\'');
                if (apos > 0 && apos < token.Length - 1)
                {
                    sb.Append(token[..apos]);   // contracted article — stays lowercase
                    sb.Append('\'');
                    sb.Append(CapFirst(token[(apos + 1)..]));
                    continue;
                }

                if (i == 0) { sb.Append(CapFirst(token)); continue; }
                if (_stopWords.Contains(token) || _commonNouns.Contains(token)) { sb.Append(token); continue; }
                sb.Append(CapFirst(token));
            }

            return sb.ToString();
        }

        private static string CapFirst(string s) =>
            s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

        private class NominatimResult
        {
            public string Lat { get; set; } = "";
            public string Lon { get; set; } = "";
            public NominatimAddress? Address { get; set; }

            public class NominatimAddress
            {
                [System.Text.Json.Serialization.JsonPropertyName("country_code")]
                public string? CountryCode { get; set; }
            }
        }
    }
}
