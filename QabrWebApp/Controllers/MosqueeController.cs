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
        private readonly ILogger<MosqueeController> _logger;

        public MosqueeController(IMosqueeService service, IMosqueeViewModelBuilder builder, IOverpassService overpass, IEmailService email, IPriereJanazaService priereService, IPushNotificationService push, IMosqueeDeduplicationService dedup, IUtilisateurService utilisateurService, ILogger<MosqueeController> logger)
        {
            _service = service;
            _builder = builder;
            _overpass = overpass;
            _email = email;
            _priereService = priereService;
            _push = push;
            _dedup = dedup;
            _utilisateurService = utilisateurService;
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
            "en" => "en",
            "ar" => "ar",
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
