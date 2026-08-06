using Microsoft.AspNetCore.Mvc;
using QabrWebApp.Builders;
using QabrWebApp.Domain.Models;
using QabrWebApp.Domain.Services;
using QabrWebApp.Request;
using QabrWebApp.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Text;

namespace QabrWebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MosqueeController : ControllerBase
    {
        private readonly IMosqueeService _service;
        private readonly IMosqueeViewModelBuilder _builder;
        private readonly IOverpassService _overpass;
        private readonly IEmailService _email;
        private readonly IPriereJanazaService _priereService;
        private readonly IPushNotificationService _push;
        private readonly IMosqueeDeduplicationService _dedup;
        private readonly IUtilisateurService _utilisateurService;
        private readonly ITelegramNotificationBuilder _telegram;
        private readonly IRattrapageLieuxService _rattrapage;
        private readonly IRattrapageLieuxWorker _worker;
        private readonly IGeocodageInverseService _geocodage;
        private readonly ILogger<MosqueeController> _logger;

        public MosqueeController(IMosqueeService service, IMosqueeViewModelBuilder builder, IOverpassService overpass, IEmailService email, IPriereJanazaService priereService, IPushNotificationService push, IMosqueeDeduplicationService dedup, IUtilisateurService utilisateurService, ITelegramNotificationBuilder telegram, IRattrapageLieuxService rattrapage, IRattrapageLieuxWorker worker, IGeocodageInverseService geocodage, ILogger<MosqueeController> logger)
        {
            _rattrapage = rattrapage;
            _worker = worker;
            _geocodage = geocodage;
            _service = service;
            _builder = builder;
            _overpass = overpass;
            _email = email;
            _priereService = priereService;
            _push = push;
            _dedup = dedup;
            _utilisateurService = utilisateurService;
            _telegram = telegram;
            _logger = logger;
        }

        [HttpGet]
        [SwaggerOperation(Summary = "Liste toutes les mosquées")]
        public async Task<IActionResult> GetAll()
        {
            var list = await _service.GetAllAsync();
            return Ok(_builder.BuildList(list));
        }

        [HttpGet("{id}")]
        [SwaggerOperation(Summary = "Récupère une mosquée par ID")]
        public async Task<IActionResult> GetById(int id)
        {
            var m = await _service.GetByIdAsync(id);
            return m is null ? NotFound() : Ok(_builder.Build(m));
        }

        [HttpGet("osm/{osmId}")]
        [SwaggerOperation(Summary = "Récupère une mosquée par OsmId")]
        public async Task<IActionResult> GetByOsmId(string osmId)
        {
            var m = await _service.GetByOsmIdAsync(osmId);
            return m is null ? NotFound() : Ok(_builder.Build(m));
        }

        [HttpGet("nearby")]
        [SwaggerOperation(Summary = "Mosquées proches d'une position depuis le cache DB")]
        public async Task<IActionResult> GetNearby([FromQuery] MosqueeNearbyRequest req)
        {
            var nearby = await _service.GetNearbyAsync(req.Latitude, req.Longitude, req.RadiusKm);
            var vms = nearby
                .Select(m =>
                {
                    var d = HaversineKm(req.Latitude, req.Longitude, m.Latitude, m.Longitude);
                    return _builder.Build(m, Math.Round(d, 1));
                })
                .OrderBy(v => v.DistanceKm)
                .ToList();
            return Ok(vms);
        }

        [HttpPost("sync-osm")]
        [SwaggerOperation(Summary = "Upsert en masse des mosquées OSM dans le cache DB")]
        public async Task<IActionResult> SyncOsm([FromBody] MosqueeSyncOsmRequest req)
        {
            if (req.Mosques.Count == 0) return NoContent();

            var mosquees = req.Mosques
                .Where(m => !string.IsNullOrEmpty(m.OsmId))
                .Select(m => new Mosquee
                {
                    Nom = m.Nom,
                    Adresse = m.Adresse,
                    Latitude = m.Latitude,
                    Longitude = m.Longitude,
                    OsmId = m.OsmId,
                })
                .ToList();

            var suppressedOsmIds = await _service.UpsertBulkFromOsmAsync(mosquees);
            if (suppressedOsmIds.Count > 0)
                return Ok(new { suppressedOsmIds });
            return NoContent();
        }

        [HttpGet("search")]
        [SwaggerOperation(Summary = "Recherche de mosquées par nom ou adresse (pour déclaration)")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return Ok(new List<object>());
            var results = await _service.SearchAsync(q);
            return Ok(_builder.BuildList(results));
        }

        [HttpGet("pending")]
        [SwaggerOperation(Summary = "Mosquées en attente de validation (admin)")]
        public async Task<IActionResult> GetPending()
        {
            var list = await _service.GetPendingAsync();
            return Ok(_builder.BuildList(list));
        }

        [HttpGet("contributions")]
        [SwaggerOperation(Summary = "Mosquées validées ajoutées par les utilisateurs")]
        public async Task<IActionResult> GetContributions()
        {
            var list = await _service.GetContributionsAsync();
            return Ok(_builder.BuildList(list));
        }

        [HttpPost]
        [SwaggerOperation(Summary = "Crée une mosquée manuellement")]
        public async Task<IActionResult> Create([FromBody] MosqueeRequest req)
        {
            var mosquee = new Mosquee
            {
                Nom = req.Nom,
                Adresse = req.Adresse,
                Latitude = req.Latitude,
                Longitude = req.Longitude,
                OsmId = req.OsmId,
            };
            await RenseignerLieuAsync(mosquee);
            var created = await _service.CreateAsync(mosquee);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, _builder.Build(created));
        }

        [HttpPost("suggestion")]
        [SwaggerOperation(Summary = "Soumet une mosquée pour validation")]
        public async Task<IActionResult> Suggestion([FromBody] MosqueeRequest req)
        {
            // Résoudre l'utilisateur depuis le JWT si le frontend n'a pas fourni l'ID
            _logger.LogInformation("Suggestion: req.UtilisateurId={Uid}, IsAuthenticated={Auth}, Claims=[{Claims}]",
                req.UtilisateurId,
                User.Identity?.IsAuthenticated,
                string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}")));

            var utilisateurId = req.UtilisateurId;
            if (utilisateurId is null || utilisateurId == 0)
            {
                var identityId = User.Claims
                    .FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier
                                      || c.Type == "sub"
                                      || c.Type.EndsWith("/nameidentifier"))?.Value;
                _logger.LogInformation("Suggestion: identityId depuis JWT = {IdentityId}", identityId);
                if (!string.IsNullOrEmpty(identityId))
                {
                    var u = await _utilisateurService.GetByIdentityIdAsync(identityId);
                    utilisateurId = u?.Id;
                    _logger.LogInformation("Suggestion: UtilisateurId résolu depuis JWT → {Id}", utilisateurId);
                }
            }

            var mosquee = new Mosquee
            {
                Nom = req.Nom,
                Adresse = req.Adresse,
                Latitude = req.Latitude,
                Longitude = req.Longitude,
                UtilisateurId = utilisateurId,
            };
            await RenseignerLieuAsync(mosquee);
            var created = await _service.CreateSuggestionAsync(mosquee);

            var html = new StringBuilder();
            html.AppendLine("<h2>Nouvelle mosquée soumise par un utilisateur</h2>");
            html.AppendLine($"<p><strong>Nom :</strong> {created.Nom}</p>");
            html.AppendLine($"<p><strong>Adresse :</strong> {created.Adresse ?? "—"}</p>");
            html.AppendLine($"<p><strong>Coordonnées :</strong> {created.Latitude}, {created.Longitude}</p>");
            html.AppendLine($"<p><strong>ID :</strong> {created.Id}</p>");
            html.AppendLine("<p>Connectez-vous à l'interface admin pour valider ou refuser cette mosquée.</p>");

            _ = _email.SendNotificationAsync("support@salatjanaza.org",
                $"[Salat Janaza] Nouvelle mosquée à valider : {created.Nom}", html.ToString());

            var utilisateurEmail = utilisateurId.HasValue
                ? (await _utilisateurService.GetByIdAsync(utilisateurId.Value))?.Email
                : null;
            _ = _telegram.NotifyPendingMosqueeAsync(created, utilisateurEmail);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, _builder.Build(created));
        }

        [HttpPut("{id}/valider")]
        [SwaggerOperation(Summary = "Valide une mosquée en attente et publie les janazas liées")]
        public async Task<IActionResult> Valider(int id)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();
            _logger.LogWarning("Valider: DÉBUT mosquée {Id} | Nom={Nom} | UtilisateurId={Uid}", id, existing.Nom, existing.UtilisateurId);
            await _service.ValiderAsync(id);

            var linked = (await _priereService.GetPendingAsync()).Where(p => p.MosqueeId == id).ToList();
            if (linked.Count > 0)
            {
                await _priereService.ActivatePendingByMosqueeAsync(id);
                foreach (var priere in linked)
                {
                    _ = _push.NotifyMosqueeSubscribersAsync(id, priere);
                    _ = _push.ScheduleMosqueeReminderAsync(id, priere);
                    _ = _push.NotifyRadiusUsersAsync(id, priere);
                }
            }

            if (existing.UtilisateurId.HasValue)
            {
                _logger.LogInformation("Valider: UtilisateurId={Id} pour mosquée {Nom}", existing.UtilisateurId.Value, existing.Nom);
                var utilisateur = await _utilisateurService.GetByIdAsync(existing.UtilisateurId.Value);
                if (utilisateur is not null && !string.IsNullOrEmpty(utilisateur.Email))
                {
                    _logger.LogInformation("Valider: envoi email à {Email}", utilisateur.Email);
                    var lang = NormalizeLang(utilisateur.Language);
                    var (subject, html) = BuildMosqueeValidatedEmail(utilisateur.Prenom, existing.Nom, existing.Adresse, lang);
                    try { await _email.SendNotificationAsync(utilisateur.Email, subject, html); }
                    catch (Exception ex) { _logger.LogError(ex, "Valider: échec envoi email à {Email}", utilisateur.Email); }
                }
                else
                {
                    _logger.LogWarning("Valider: utilisateur {Id} introuvable ou email vide", existing.UtilisateurId.Value);
                }
            }
            else
            {
                _logger.LogWarning("Valider: mosquée {Id} sans UtilisateurId, pas d'email envoyé", id);
            }

            return NoContent();
        }

        [HttpPut("{id}")]
        [SwaggerOperation(Summary = "Met à jour une mosquée")]
        public async Task<IActionResult> Update(int id, [FromBody] MosqueeRequest req)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();
            existing.Nom = req.Nom;
            existing.Adresse = req.Adresse;
            existing.Latitude = req.Latitude;
            existing.Longitude = req.Longitude;
            existing.OsmId = req.OsmId ?? existing.OsmId;
            var updated = await _service.UpdateAsync(existing);
            return Ok(_builder.Build(updated));
        }

        [HttpPut("{id}/refuser")]
        [SwaggerOperation(Summary = "Refuse une mosquée en attente et notifie le soumetteur par email")]
        public async Task<IActionResult> Refuser(int id)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();

            if (existing.UtilisateurId.HasValue)
            {
                _logger.LogInformation("Refuser: UtilisateurId={Id} pour mosquée {Nom}", existing.UtilisateurId.Value, existing.Nom);
                var utilisateur = await _utilisateurService.GetByIdAsync(existing.UtilisateurId.Value);
                if (utilisateur is not null && !string.IsNullOrEmpty(utilisateur.Email))
                {
                    _logger.LogInformation("Refuser: envoi email à {Email}", utilisateur.Email);
                    var lang = NormalizeLang(utilisateur.Language);
                    var (subject, html) = BuildMosqueeRefusedEmail(utilisateur.Prenom, existing.Nom, existing.Adresse, lang);
                    try { await _email.SendNotificationAsync(utilisateur.Email, subject, html); }
                    catch (Exception ex) { _logger.LogError(ex, "Refuser: échec envoi email à {Email}", utilisateur.Email); }
                }
                else
                {
                    _logger.LogWarning("Refuser: utilisateur {Id} introuvable ou email vide", existing.UtilisateurId.Value);
                }
            }
            else
            {
                _logger.LogWarning("Refuser: mosquée {Id} sans UtilisateurId, pas d'email envoyé", id);
            }

            // Supprimer toutes les janazas en attente liées à cette mosquée —
            // elles n'ont de sens que si la mosquée est validée.
            var janazasEnAttente = await _priereService.GetByMosqueeIdAsync(id);
            foreach (var j in janazasEnAttente.Where(j => j.Statut == StatutPriere.EnAttente))
                await _priereService.DeleteAsync(j.Id);

            await _service.DeleteAsync(id);
            return NoContent();
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(Summary = "Supprime une mosquée")]
        public async Task<IActionResult> Delete(int id)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();
            await _service.DeleteAsync(id);
            return NoContent();
        }

        [HttpPost("deduplicate")]
        [SwaggerOperation(Summary = "Supprime les mosquées en doublon (même coordonnées GPS), conserve la plus récente")]
        public async Task<IActionResult> Deduplicate(CancellationToken ct)
        {
            var (groupes, supprimees) = await _dedup.SupprimerDoublonsAsync(ct);
            return Ok(new { groupesTraites = groupes, mosqueesSupprimees = supprimees });
        }

        [HttpPost("normaliser-sans-nom")]
        [SwaggerOperation(Summary = "Renomme les mosquées 'Mosquée' d'après leur ville, supprime celles sans adresse valide")]
        public async Task<IActionResult> NormaliserSansNom()
        {
            var result = await _service.NormaliserSansNomAsync();
            return Ok(result);
        }

        /// <summary>
        /// L'avancement du rattrapage : combien de mosquées attendent encore
        /// leur ville, et si un traitement est en cours.
        /// </summary>
        [HttpGet("lieux/etat")]
        [SwaggerOperation(Summary = "Avancement du rattrapage des villes et pays")]
        public async Task<IActionResult> EtatLieux(CancellationToken ct)
            => Ok(await _worker.EtatAsync(ct));

        /// <summary>
        /// Donne le départ du rattrapage et rend la main IMMÉDIATEMENT.
        ///
        /// Le géocodage est cadencé à une requête par seconde : sept cents
        /// mosquées demandent une douzaine de minutes. Faire attendre la
        /// requête HTTP tout ce temps obligerait l'application à rester au
        /// premier plan, écran allumé, sans quoi la chaîne se casse. Le serveur
        /// travaille donc seul, et l'administration interroge `lieux/etat`
        /// quand elle veut — ou ferme l'écran sans rien interrompre.
        /// </summary>
        [HttpPost("lieux/rattrapage")]
        [SwaggerOperation(Summary = "Lance en fond le géocodage inverse des mosquées sans ville")]
        public async Task<IActionResult> RattrapageLieux(CancellationToken ct)
        {
            var demarre = _worker.Demarrer();
            var etat = await _worker.EtatAsync(ct);

            // 202 et non 200 : le travail est accepté, pas terminé.
            return Accepted(new { demarre, etat });
        }

        /// <summary>
        /// Renseigne ville et pays avant l'enregistrement, par géocodage
        /// inverse des coordonnées.
        ///
        /// Plafonné à cinq secondes, et sans jamais faire échouer la création :
        /// une mosquée déclarée par un utilisateur ne doit pas être perdue
        /// parce qu'un service tiers gratuit est lent ou indisponible. Ce qui
        /// n'est pas résolu ici le sera au prochain rattrapage, qui ne traite
        /// justement que les lignes sans ville.
        ///
        /// L'import OSM en masse ne passe volontairement pas par ici : à une
        /// requête par seconde, plusieurs centaines de mosquées dépasseraient
        /// tout délai d'attente. C'est le rattrapage qui les prend en charge.
        /// </summary>
        private async Task RenseignerLieuAsync(Mosquee mosquee)
        {
            try
            {
                using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var lieu = await _geocodage.ResoudreAsync(mosquee.Latitude, mosquee.Longitude, delai.Token);
                mosquee.Ville = lieu?.Ville;
                mosquee.Pays = lieu?.Pays;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Géocodage inverse indisponible à la création de « {Nom} »", mosquee.Nom);
            }
        }

        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static string NormalizeLang(string? lang) => lang?.ToLower() switch
        {
            "en" => "en", "ar" => "ar", "tr" => "tr", "de" => "de",
            "es" => "es", "it" => "it", "pt" => "pt", "ru" => "ru",
            "ja" => "ja", "ko" => "ko", "ms" => "ms", "id" => "id",
            "bn" => "bn", "ur" => "ur", "bm" => "bm", "nl" => "nl",
            _ => "fr"
        };

        private static (string subject, string html) BuildMosqueeValidatedEmail(string prenom, string mosqueeNom, string? adresse, string lang)
        {
            var adresseHtml = !string.IsNullOrEmpty(adresse) ? $"<span style=\"color:#555;\">{adresse}</span>" : "";
            return lang switch
            {
                "en" => (
                    $"[Salat Janaza] Your mosque \"{mosqueeNom}\" has been approved",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ Your mosque has been approved!</h2>
                    <p>Hello {prenom},</p>
                    <p>Great news! The mosque you submitted has been approved by our team and is now visible to all Salat Janaza users.</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Thank you for your contribution to the community!</p>
                    <p style="color:#888;font-size:13px;">The Salat Janaza team</p>
                    </div>
                    """
                ),
                "ar" => (
                    $"[Salat Janaza] تمت الموافقة على مسجدك \"{mosqueeNom}\"",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;direction:rtl;text-align:right;">
                    <h2 style="color:#238636;">✅ تمت الموافقة على مسجدك!</h2>
                    <p>مرحباً {prenom}،</p>
                    <p>خبر رائع! لقد تمت الموافقة على المسجد الذي أرسلته من قِبل فريقنا وأصبح مرئياً لجميع مستخدمي صلاة الجنازة.</p>
                    <div style="background:#f0f9f2;border-right:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>شكراً لمساهمتك في خدمة المجتمع!</p>
                    <p style="color:#888;font-size:13px;">فريق صلاة الجنازة</p>
                    </div>
                    """
                ),
                "tr" => (
                    $"[Salat Janaza] Camınız \"{mosqueeNom}\" onaylandı",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ Camınız onaylandı!</h2>
                    <p>Merhaba {prenom},</p>
                    <p>Harika haber! Gönderdiğiniz cami ekibimiz tarafından onaylandı ve artık tüm Salat Janaza kullanıcıları tarafından görülebilir.</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Topluluğa katkınız için teşekkür ederiz!</p>
                    <p style="color:#888;font-size:13px;">Salat Janaza ekibi</p>
                    </div>
                    """
                ),
                "de" => (
                    $"[Salat Janaza] Ihre Moschee \"{mosqueeNom}\" wurde genehmigt",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ Ihre Moschee wurde genehmigt!</h2>
                    <p>Hallo {prenom},</p>
                    <p>Gute Neuigkeiten! Die von Ihnen eingereichte Moschee wurde von unserem Team genehmigt und ist jetzt für alle Salat Janaza-Nutzer sichtbar.</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Vielen Dank für Ihren Beitrag zur Gemeinschaft!</p>
                    <p style="color:#888;font-size:13px;">Das Salat Janaza-Team</p>
                    </div>
                    """
                ),
                "es" => (
                    $"[Salat Janaza] Tu mezquita \"{mosqueeNom}\" ha sido aprobada",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ ¡Tu mezquita ha sido aprobada!</h2>
                    <p>Hola {prenom},</p>
                    <p>¡Buenas noticias! La mezquita que enviaste ha sido aprobada por nuestro equipo y ahora es visible para todos los usuarios de Salat Janaza.</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>¡Gracias por tu contribución a la comunidad!</p>
                    <p style="color:#888;font-size:13px;">El equipo de Salat Janaza</p>
                    </div>
                    """
                ),
                "it" => (
                    $"[Salat Janaza] La tua moschea \"{mosqueeNom}\" è stata approvata",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ La tua moschea è stata approvata!</h2>
                    <p>Ciao {prenom},</p>
                    <p>Ottime notizie! La moschea che hai inviato è stata approvata dal nostro team ed è ora visibile a tutti gli utenti di Salat Janaza.</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Grazie per il tuo contributo alla comunità!</p>
                    <p style="color:#888;font-size:13px;">Il team di Salat Janaza</p>
                    </div>
                    """
                ),
                "pt" => (
                    $"[Salat Janaza] Sua mesquita \"{mosqueeNom}\" foi aprovada",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ Sua mesquita foi aprovada!</h2>
                    <p>Olá {prenom},</p>
                    <p>Ótimas notícias! A mesquita que você enviou foi aprovada por nossa equipe e agora está visível para todos os usuários do Salat Janaza.</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Obrigado pela sua contribuição à comunidade!</p>
                    <p style="color:#888;font-size:13px;">A equipe do Salat Janaza</p>
                    </div>
                    """
                ),
                "ru" => (
                    $"[Salat Janaza] Ваша мечеть \"{mosqueeNom}\" одобрена",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ Ваша мечеть одобрена!</h2>
                    <p>Здравствуйте, {prenom},</p>
                    <p>Отличная новость! Мечеть, которую вы отправили, одобрена нашей командой и теперь видна всем пользователям Salat Janaza.</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Спасибо за ваш вклад в сообщество!</p>
                    <p style="color:#888;font-size:13px;">Команда Salat Janaza</p>
                    </div>
                    """
                ),
                "ja" => (
                    $"[Salat Janaza] モスク「{mosqueeNom}」が承認されました",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ モスクが承認されました！</h2>
                    <p>{prenom}様、</p>
                    <p>おめでとうございます！投稿されたモスクがチームによって承認され、すべてのSalat Janazaユーザーに表示されるようになりました。</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>コミュニティへのご貢献ありがとうございます！</p>
                    <p style="color:#888;font-size:13px;">Salat Janazaチーム</p>
                    </div>
                    """
                ),
                "ko" => (
                    $"[Salat Janaza] 모스크 \"{mosqueeNom}\"이(가) 승인되었습니다",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ 모스크가 승인되었습니다!</h2>
                    <p>안녕하세요 {prenom}님,</p>
                    <p>좋은 소식입니다! 제출하신 모스크가 팀에 의해 승인되어 이제 모든 Salat Janaza 사용자에게 표시됩니다.</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>커뮤니티에 기여해 주셔서 감사합니다!</p>
                    <p style="color:#888;font-size:13px;">Salat Janaza 팀</p>
                    </div>
                    """
                ),
                "ms" => (
                    $"[Salat Janaza] Masjid anda \"{mosqueeNom}\" telah diluluskan",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ Masjid anda telah diluluskan!</h2>
                    <p>Helo {prenom},</p>
                    <p>Berita baik! Masjid yang anda hantar telah diluluskan oleh pasukan kami dan kini boleh dilihat oleh semua pengguna Salat Janaza.</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Terima kasih atas sumbangan anda kepada komuniti!</p>
                    <p style="color:#888;font-size:13px;">Pasukan Salat Janaza</p>
                    </div>
                    """
                ),
                "id" => (
                    $"[Salat Janaza] Masjid Anda \"{mosqueeNom}\" telah disetujui",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ Masjid Anda telah disetujui!</h2>
                    <p>Halo {prenom},</p>
                    <p>Kabar baik! Masjid yang Anda kirimkan telah disetujui oleh tim kami dan sekarang terlihat oleh semua pengguna Salat Janaza.</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Terima kasih atas kontribusi Anda kepada komunitas!</p>
                    <p style="color:#888;font-size:13px;">Tim Salat Janaza</p>
                    </div>
                    """
                ),
                "bn" => (
                    $"[Salat Janaza] আপনার মসজিদ \"{mosqueeNom}\" অনুমোদিত হয়েছে",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ আপনার মসজিদ অনুমোদিত হয়েছে!</h2>
                    <p>হ্যালো {prenom},</p>
                    <p>দারুণ খবর! আপনার জমা দেওয়া মসজিদ আমাদের দল অনুমোদন করেছে এবং এখন সমস্ত Salat Janaza ব্যবহারকারীরা এটি দেখতে পাবেন।</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>সম্প্রদায়ে আপনার অবদানের জন্য ধন্যবাদ!</p>
                    <p style="color:#888;font-size:13px;">Salat Janaza দল</p>
                    </div>
                    """
                ),
                "ur" => (
                    $"[Salat Janaza] آپ کی مسجد \"{mosqueeNom}\" منظور ہو گئی ہے",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;direction:rtl;text-align:right;">
                    <h2 style="color:#238636;">✅ آپ کی مسجد منظور ہو گئی ہے!</h2>
                    <p>السلام علیکم {prenom}،</p>
                    <p>خوشخبری! آپ کی جمع کردہ مسجد ہماری ٹیم نے منظور کر لی ہے اور اب یہ تمام Salat Janaza صارفین کو نظر آئے گی۔</p>
                    <div style="background:#f0f9f2;border-right:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>کمیونٹی میں آپ کی شراکت کے لیے شکریہ!</p>
                    <p style="color:#888;font-size:13px;">Salat Janaza ٹیم</p>
                    </div>
                    """
                ),
                "bm" => (
                    $"[Salat Janaza] I ka misiri \"{mosqueeNom}\" sɔrɔlen don",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ I ka misiri sɔrɔlen don!</h2>
                    <p>I ni ce {prenom},</p>
                    <p>Kibaru ɲuman! I tun ciinin misiri nin sɔrɔlen don an ka sɛgɛsɛgɛliw fɛ, a filɛlen don Salat Janaza ka baara kɛlɛw bɛɛ ye sisan.</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>I ni ce k'i dɛmɛ sɔrɔ jamana na!</p>
                    <p style="color:#888;font-size:13px;">Salat Janaza ka cɛfɛla</p>
                    </div>
                    """
                ),
                "nl" => (
                    $"[Salat Janaza] Uw moskee \"{mosqueeNom}\" is goedgekeurd",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ Uw moskee is goedgekeurd!</h2>
                    <p>Hallo {prenom},</p>
                    <p>Goed nieuws! De moskee die u heeft ingediend is goedgekeurd door ons team en is nu zichtbaar voor alle Salat Janaza-gebruikers.</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Dank u voor uw bijdrage aan de gemeenschap!</p>
                    <p style="color:#888;font-size:13px;">Het Salat Janaza-team</p>
                    </div>
                    """
                ),
                _ => (
                    $"[Salat Janaza] Votre mosquée \"{mosqueeNom}\" a été validée",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#238636;">✅ Votre mosquée a été validée !</h2>
                    <p>Bonjour {prenom},</p>
                    <p>Bonne nouvelle ! La mosquée que vous avez soumise a été validée par notre équipe et est maintenant visible par tous les utilisateurs de Salat Janaza.</p>
                    <div style="background:#f0f9f2;border-left:4px solid #238636;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Merci pour votre contribution à la communauté !</p>
                    <p style="color:#888;font-size:13px;">L'équipe Salat Janaza</p>
                    </div>
                    """
                )
            };
        }

        private static (string subject, string html) BuildMosqueeRefusedEmail(string prenom, string mosqueeNom, string? adresse, string lang)
        {
            var adresseHtml = !string.IsNullOrEmpty(adresse) ? $"<span style=\"color:#555;\">{adresse}</span>" : "";
            return lang switch
            {
                "en" => (
                    $"[Salat Janaza] Your mosque \"{mosqueeNom}\" was not accepted",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ Your mosque submission was not accepted</h2>
                    <p>Hello {prenom},</p>
                    <p>After review by our team, the mosque you submitted could not be approved as it does not meet our compliance criteria.</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Possible reasons for non-compliance include:</p>
                    <ul style="color:#555;">
                    <li>Incorrect or incomplete information</li>
                    <li>Location not identifiable on the map</li>
                    <li>Duplicate of an existing location</li>
                    </ul>
                    <p>If you believe this is an error, you can resubmit your request with more precise information.</p>
                    <p style="color:#888;font-size:13px;">The Salat Janaza team</p>
                    </div>
                    """
                ),
                "ar" => (
                    $"[Salat Janaza] لم يتم قبول مسجدك \"{mosqueeNom}\"",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;direction:rtl;text-align:right;">
                    <h2 style="color:#dc2626;">❌ لم يتم قبول طلب إضافة المسجد</h2>
                    <p>مرحباً {prenom}،</p>
                    <p>بعد مراجعة فريقنا، تعذّر قبول المسجد الذي أرسلته لعدم استيفائه معايير الامتثال لدينا.</p>
                    <div style="background:#fef2f2;border-right:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>الأسباب المحتملة لعدم الامتثال:</p>
                    <ul style="color:#555;">
                    <li>معلومات غير صحيحة أو غير مكتملة</li>
                    <li>الموقع غير قابل للتعرف على الخريطة</li>
                    <li>تكرار لموقع موجود مسبقاً</li>
                    </ul>
                    <p>إذا كنت تعتقد أن هذا خطأ، يمكنك إعادة تقديم طلبك بمعلومات أكثر دقة.</p>
                    <p style="color:#888;font-size:13px;">فريق صلاة الجنازة</p>
                    </div>
                    """
                ),
                "tr" => (
                    $"[Salat Janaza] Camınız \"{mosqueeNom}\" kabul edilmedi",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ Cami başvurunuz kabul edilmedi</h2>
                    <p>Merhaba {prenom},</p>
                    <p>Ekibimizin incelemesi sonucunda gönderdiğiniz cami, uyumluluk kriterlerimizi karşılamadığı için onaylanamadı.</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Olası uyumsuzluk nedenleri:</p>
                    <ul style="color:#555;">
                    <li>Hatalı veya eksik bilgiler</li>
                    <li>Haritada tanımlanamayan konum</li>
                    <li>Mevcut bir konumun tekrarı</li>
                    </ul>
                    <p>Bir hata olduğunu düşünüyorsanız, daha kesin bilgilerle talebinizi yeniden gönderebilirsiniz.</p>
                    <p style="color:#888;font-size:13px;">Salat Janaza ekibi</p>
                    </div>
                    """
                ),
                "de" => (
                    $"[Salat Janaza] Ihre Moschee \"{mosqueeNom}\" wurde nicht akzeptiert",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ Ihr Moscheenantrag wurde nicht akzeptiert</h2>
                    <p>Hallo {prenom},</p>
                    <p>Nach Prüfung durch unser Team konnte die eingereichte Moschee nicht genehmigt werden, da sie unsere Konformitätskriterien nicht erfüllt.</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Mögliche Gründe für die Ablehnung:</p>
                    <ul style="color:#555;">
                    <li>Falsche oder unvollständige Informationen</li>
                    <li>Standort auf der Karte nicht identifizierbar</li>
                    <li>Duplikat eines bereits vorhandenen Standorts</li>
                    </ul>
                    <p>Wenn Sie glauben, dass dies ein Fehler ist, können Sie Ihren Antrag mit genaueren Informationen erneut einreichen.</p>
                    <p style="color:#888;font-size:13px;">Das Salat Janaza-Team</p>
                    </div>
                    """
                ),
                "es" => (
                    $"[Salat Janaza] Tu mezquita \"{mosqueeNom}\" no fue aceptada",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ Tu solicitud de mezquita no fue aceptada</h2>
                    <p>Hola {prenom},</p>
                    <p>Tras la revisión de nuestro equipo, la mezquita que enviaste no pudo ser aprobada porque no cumple con nuestros criterios de conformidad.</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Las posibles razones de no conformidad incluyen:</p>
                    <ul style="color:#555;">
                    <li>Información incorrecta o incompleta</li>
                    <li>Ubicación no identificable en el mapa</li>
                    <li>Duplicado de un lugar ya existente</li>
                    </ul>
                    <p>Si crees que es un error, puedes volver a enviar tu solicitud con información más precisa.</p>
                    <p style="color:#888;font-size:13px;">El equipo de Salat Janaza</p>
                    </div>
                    """
                ),
                "it" => (
                    $"[Salat Janaza] La tua moschea \"{mosqueeNom}\" non è stata accettata",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ La tua richiesta di moschea non è stata accettata</h2>
                    <p>Ciao {prenom},</p>
                    <p>Dopo la revisione del nostro team, la moschea che hai inviato non ha potuto essere approvata perché non soddisfa i nostri criteri di conformità.</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Le possibili ragioni di non conformità includono:</p>
                    <ul style="color:#555;">
                    <li>Informazioni errate o incomplete</li>
                    <li>Posizione non identificabile sulla mappa</li>
                    <li>Duplicato di un luogo già esistente</li>
                    </ul>
                    <p>Se pensi che si tratti di un errore, puoi reinviare la tua richiesta con informazioni più precise.</p>
                    <p style="color:#888;font-size:13px;">Il team di Salat Janaza</p>
                    </div>
                    """
                ),
                "pt" => (
                    $"[Salat Janaza] Sua mesquita \"{mosqueeNom}\" não foi aceita",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ Sua solicitação de mesquita não foi aceita</h2>
                    <p>Olá {prenom},</p>
                    <p>Após análise da nossa equipe, a mesquita que você enviou não pôde ser aprovada por não atender aos nossos critérios de conformidade.</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Os possíveis motivos de não conformidade incluem:</p>
                    <ul style="color:#555;">
                    <li>Informações incorretas ou incompletas</li>
                    <li>Local não identificável no mapa</li>
                    <li>Duplicata de um local já existente</li>
                    </ul>
                    <p>Se você acredita que é um erro, pode reenviar sua solicitação com informações mais precisas.</p>
                    <p style="color:#888;font-size:13px;">A equipe do Salat Janaza</p>
                    </div>
                    """
                ),
                "ru" => (
                    $"[Salat Janaza] Ваша мечеть \"{mosqueeNom}\" не была принята",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ Ваша заявка на мечеть не была принята</h2>
                    <p>Здравствуйте, {prenom},</p>
                    <p>После проверки нашей командой поданная вами мечеть не может быть одобрена, так как не соответствует нашим критериям.</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Возможные причины несоответствия:</p>
                    <ul style="color:#555;">
                    <li>Неверная или неполная информация</li>
                    <li>Местоположение не определяется на карте</li>
                    <li>Дублирование уже существующего места</li>
                    </ul>
                    <p>Если вы считаете, что это ошибка, вы можете повторно подать заявку с более точными данными.</p>
                    <p style="color:#888;font-size:13px;">Команда Salat Janaza</p>
                    </div>
                    """
                ),
                "ja" => (
                    $"[Salat Janaza] モスク「{mosqueeNom}」は承認されませんでした",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ モスクの申請は承認されませんでした</h2>
                    <p>{prenom}様、</p>
                    <p>チームの審査の結果、提出されたモスクは当社の基準を満たしていないため、承認できませんでした。</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>不承認の理由として考えられるもの：</p>
                    <ul style="color:#555;">
                    <li>不正確または不完全な情報</li>
                    <li>地図上で場所を特定できない</li>
                    <li>既存の場所との重複</li>
                    </ul>
                    <p>誤りだと思われる場合は、より正確な情報で再申請できます。</p>
                    <p style="color:#888;font-size:13px;">Salat Janazaチーム</p>
                    </div>
                    """
                ),
                "ko" => (
                    $"[Salat Janaza] 모스크 \"{mosqueeNom}\"이(가) 수락되지 않았습니다",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ 모스크 신청이 수락되지 않았습니다</h2>
                    <p>안녕하세요 {prenom}님,</p>
                    <p>팀의 검토 결과, 제출하신 모스크가 준수 기준을 충족하지 않아 승인할 수 없었습니다.</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>미준수의 가능한 이유:</p>
                    <ul style="color:#555;">
                    <li>잘못되거나 불완전한 정보</li>
                    <li>지도에서 위치를 식별할 수 없음</li>
                    <li>기존 장소의 중복</li>
                    </ul>
                    <p>오류라고 생각하신다면 더 정확한 정보로 다시 신청하실 수 있습니다.</p>
                    <p style="color:#888;font-size:13px;">Salat Janaza 팀</p>
                    </div>
                    """
                ),
                "ms" => (
                    $"[Salat Janaza] Masjid anda \"{mosqueeNom}\" tidak diterima",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ Permohonan masjid anda tidak diterima</h2>
                    <p>Helo {prenom},</p>
                    <p>Selepas semakan oleh pasukan kami, masjid yang anda hantar tidak dapat diluluskan kerana tidak memenuhi kriteria pematuhan kami.</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Kemungkinan sebab ketidakpatuhan:</p>
                    <ul style="color:#555;">
                    <li>Maklumat yang salah atau tidak lengkap</li>
                    <li>Lokasi tidak dapat dikenal pasti pada peta</li>
                    <li>Pendua lokasi yang sudah wujud</li>
                    </ul>
                    <p>Jika anda percaya ini adalah kesilapan, anda boleh menghantar semula permohonan anda dengan maklumat yang lebih tepat.</p>
                    <p style="color:#888;font-size:13px;">Pasukan Salat Janaza</p>
                    </div>
                    """
                ),
                "id" => (
                    $"[Salat Janaza] Masjid Anda \"{mosqueeNom}\" tidak diterima",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ Pengajuan masjid Anda tidak diterima</h2>
                    <p>Halo {prenom},</p>
                    <p>Setelah ditinjau oleh tim kami, masjid yang Anda kirimkan tidak dapat disetujui karena tidak memenuhi kriteria kepatuhan kami.</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Kemungkinan alasan ketidakpatuhan meliputi:</p>
                    <ul style="color:#555;">
                    <li>Informasi yang salah atau tidak lengkap</li>
                    <li>Lokasi tidak dapat diidentifikasi di peta</li>
                    <li>Duplikat lokasi yang sudah ada</li>
                    </ul>
                    <p>Jika Anda pikir ini adalah kesalahan, Anda dapat mengajukan kembali permintaan Anda dengan informasi yang lebih tepat.</p>
                    <p style="color:#888;font-size:13px;">Tim Salat Janaza</p>
                    </div>
                    """
                ),
                "bn" => (
                    $"[Salat Janaza] আপনার মসজিদ \"{mosqueeNom}\" গ্রহণ করা হয়নি",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ আপনার মসজিদের আবেদন গ্রহণ করা হয়নি</h2>
                    <p>হ্যালো {prenom},</p>
                    <p>আমাদের দলের পর্যালোচনার পরে, আপনার জমা দেওয়া মসজিদ আমাদের মানদণ্ড পূরণ না করায় অনুমোদন করা যায়নি।</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>সম্ভাব্য কারণ:</p>
                    <ul style="color:#555;">
                    <li>ভুল বা অসম্পূর্ণ তথ্য</li>
                    <li>মানচিত্রে অবস্থান শনাক্তযোগ্য নয়</li>
                    <li>বিদ্যমান স্থানের সাথে নকল</li>
                    </ul>
                    <p>যদি মনে করেন এটি একটি ভুল, আপনি আরও সঠিক তথ্য দিয়ে আবার আবেদন করতে পারেন।</p>
                    <p style="color:#888;font-size:13px;">Salat Janaza দল</p>
                    </div>
                    """
                ),
                "ur" => (
                    $"[Salat Janaza] آپ کی مسجد \"{mosqueeNom}\" قبول نہیں ہوئی",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;direction:rtl;text-align:right;">
                    <h2 style="color:#dc2626;">❌ آپ کی مسجد کی درخواست قبول نہیں ہوئی</h2>
                    <p>السلام علیکم {prenom}،</p>
                    <p>ہماری ٹیم کے جائزے کے بعد، آپ کی جمع کردہ مسجد کو ہمارے معیارات پر پورا نہ اترنے کی وجہ سے منظور نہیں کیا جا سکا۔</p>
                    <div style="background:#fef2f2;border-right:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>ممکنہ وجوہات:</p>
                    <ul style="color:#555;">
                    <li>غلط یا نامکمل معلومات</li>
                    <li>نقشے پر مقام قابل شناخت نہیں</li>
                    <li>پہلے سے موجود جگہ کی نقل</li>
                    </ul>
                    <p>اگر آپ کو لگتا ہے کہ یہ غلطی ہے، تو آپ زیادہ درست معلومات کے ساتھ دوبارہ درخواست دے سکتے ہیں۔</p>
                    <p style="color:#888;font-size:13px;">Salat Janaza ٹیم</p>
                    </div>
                    """
                ),
                "bm" => (
                    $"[Salat Janaza] I ka misiri \"{mosqueeNom}\" sɔrɔlen ma kɛ",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ I ka misiri ɲinigali sɔrɔlen ma kɛ</h2>
                    <p>I ni ce {prenom},</p>
                    <p>An ka sɛgɛsɛgɛ kɛ ka ban, i tun ciinin misiri nin sɔrɔlen tɛ, a ma se an ka sɔrɔ-kɛcogo kɔlɔsili ye.</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Sabuw minw bɛ se ka kɛ:</p>
                    <ul style="color:#555;">
                    <li>Kunnafoni tɛ tiɲɛ wala a tɛ gafe la</li>
                    <li>Yɔrɔ bɛ se ka sɔrɔ carte kan</li>
                    <li>Yɔrɔ wɛrɛ bɛ yen kɔfɛ</li>
                    </ul>
                    <p>N'i miirila ko o ye fili ye, i bɛ se ka i ka ɲinigali segin ni kunnafoni tɔgɔminɛnw ye.</p>
                    <p style="color:#888;font-size:13px;">Salat Janaza ka cɛfɛla</p>
                    </div>
                    """
                ),
                "nl" => (
                    $"[Salat Janaza] Uw moskee \"{mosqueeNom}\" werd niet geaccepteerd",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ Uw moskeeaanvraag werd niet geaccepteerd</h2>
                    <p>Hallo {prenom},</p>
                    <p>Na beoordeling door ons team kon de ingediende moskee niet worden goedgekeurd omdat deze niet voldoet aan onze conformiteitscriteria.</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Mogelijke redenen voor niet-conformiteit zijn:</p>
                    <ul style="color:#555;">
                    <li>Onjuiste of onvolledige informatie</li>
                    <li>Locatie niet identificeerbaar op de kaart</li>
                    <li>Duplicaat van een bestaande locatie</li>
                    </ul>
                    <p>Als u denkt dat dit een vergissing is, kunt u uw verzoek opnieuw indienen met nauwkeurigere informatie.</p>
                    <p style="color:#888;font-size:13px;">Het Salat Janaza-team</p>
                    </div>
                    """
                ),
                _ => (
                    $"[Salat Janaza] Votre mosquée \"{mosqueeNom}\" n'a pas été acceptée",
                    $"""
                    <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                    <h2 style="color:#dc2626;">❌ Votre demande d'ajout de mosquée n'a pas été acceptée</h2>
                    <p>Bonjour {prenom},</p>
                    <p>Après examen par notre équipe, la mosquée que vous avez soumise n'a pas pu être validée car elle ne répond pas à nos critères de conformité.</p>
                    <div style="background:#fef2f2;border-left:4px solid #dc2626;padding:12px 16px;margin:16px 0;border-radius:4px;">
                    <strong>🕌 {mosqueeNom}</strong><br/>{adresseHtml}
                    </div>
                    <p>Les raisons possibles de non-conformité incluent :</p>
                    <ul style="color:#555;">
                    <li>Informations incorrectes ou incomplètes</li>
                    <li>Lieu non identifiable sur la carte</li>
                    <li>Doublon avec un lieu déjà existant</li>
                    </ul>
                    <p>Si vous pensez qu'il s'agit d'une erreur, vous pouvez soumettre à nouveau votre demande avec des informations plus précises.</p>
                    <p style="color:#888;font-size:13px;">L'équipe Salat Janaza</p>
                    </div>
                    """
                )
            };
        }
    }
}
