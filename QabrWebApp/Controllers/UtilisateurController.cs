using Microsoft.AspNetCore.Mvc;
using MimeKit;
using QabrWebApp.Builders;
using QabrWebApp.Domain.Models;
using QabrWebApp.Domain.Repositories;
using QabrWebApp.Domain.Services;
using QabrWebApp.Request;
using QabrWebApp.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Net.Http.Json;

namespace QabrWebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UtilisateurController : ControllerBase
    {
        private readonly IUtilisateurService _service;
        private readonly IUtilisateurViewModelBuilder _builder;
        private readonly IEmailService _emailService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<UtilisateurController> _logger;
        private readonly IUtilisateurTokenRepository _tokenRepo;
        private readonly IPushNotificationService _push;
        private readonly IServiceScopeFactory _scopeFactory;

        public UtilisateurController(IUtilisateurService service, IUtilisateurViewModelBuilder builder, IEmailService emailService, IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<UtilisateurController> logger, IUtilisateurTokenRepository tokenRepo, IPushNotificationService push, IServiceScopeFactory scopeFactory)
        {
            _service = service;
            _builder = builder;
            _emailService = emailService;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
            _tokenRepo = tokenRepo;
            _push = push;
            _scopeFactory = scopeFactory;
        }

        [HttpGet]
        [SwaggerOperation(Summary = "Liste tous les utilisateurs")]
        public async Task<IActionResult> GetAll()
        {
            var list = await _service.GetAllAsync();
            return Ok(_builder.BuildList(list));
        }

        [HttpGet("{id}")]
        [SwaggerOperation(Summary = "Récupère un utilisateur par ID")]
        public async Task<IActionResult> GetById(int id)
        {
            var u = await _service.GetByIdAsync(id);
            return u is null ? NotFound() : Ok(_builder.Build(u));
        }

        [HttpGet("identity/{identityUserId}")]
        [SwaggerOperation(Summary = "Récupère un utilisateur par son IdentityUserId")]
        public async Task<IActionResult> GetByIdentityId(string identityUserId)
        {
            var u = await _service.GetByIdentityIdAsync(identityUserId);
            return u is null ? NotFound() : Ok(_builder.Build(u));
        }

        [HttpPost]
        [SwaggerOperation(Summary = "Crée un profil utilisateur (appelé après inscription IdentityServer)")]
        public async Task<IActionResult> Create([FromBody] UtilisateurRequest req)
        {
            if (!string.IsNullOrEmpty(req.IdentityUserId))
            {
                var existing = await _service.GetByIdentityIdAsync(req.IdentityUserId);
                if (existing is not null)
                    return Ok(_builder.Build(existing));
            }

            var utilisateur = new Utilisateur
            {
                IdentityUserId = req.IdentityUserId,
                Prenom = req.Prenom,
                Nom = req.Nom,
                Email = req.Email,
                Telephone = req.Telephone,
                RayonNotification = 5,
                Language = NormalizeLanguage(req.Language),
            };
            var created = await _service.CreateAsync(utilisateur);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, _builder.Build(created));
        }

        [HttpPut("{id}")]
        [SwaggerOperation(Summary = "Met à jour le profil utilisateur")]
        public async Task<IActionResult> Update(int id, [FromBody] UtilisateurUpdateRequest req)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();
            if (req.Prenom is not null) existing.Prenom = req.Prenom;
            if (req.Nom is not null) existing.Nom = req.Nom;
            if (req.Telephone is not null) existing.Telephone = req.Telephone;
            if (req.ExpoToken is not null)
            {
                existing.ExpoToken = req.ExpoToken;
                await _tokenRepo.UpsertAsync(id, req.ExpoToken);
            }
            if (req.AdresseDomicile is not null) existing.AdresseDomicile = req.AdresseDomicile;
            if (req.LatitudeDomicile.HasValue) existing.LatitudeDomicile = req.LatitudeDomicile;
            if (req.LongitudeDomicile.HasValue) existing.LongitudeDomicile = req.LongitudeDomicile;
            if (req.RayonNotification.HasValue) existing.RayonNotification = req.RayonNotification.Value;
            if (req.NotifMouvement.HasValue) existing.NotifMouvement = req.NotifMouvement.Value;
            if (req.LatitudeCourante.HasValue) existing.LatitudeCourante = req.LatitudeCourante;
            if (req.LongitudeCourante.HasValue) existing.LongitudeCourante = req.LongitudeCourante;
            if (req.ModeLocalisation is not null) existing.ModeLocalisation = req.ModeLocalisation;
            if (req.Platform is not null) existing.Platform = req.Platform;
            if (req.Language is not null)
            {
                existing.Language = NormalizeLanguage(req.Language);
                if (!string.IsNullOrEmpty(existing.IdentityUserId))
                {
                    var identityId = existing.IdentityUserId;
                    var lang = existing.Language;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var client = _httpClientFactory.CreateClient("identity");
                            var apiKey = _config["IdentityServer:InternalApiKey"] ?? "";
                            var httpReq = new HttpRequestMessage(HttpMethod.Put, $"/api/auth/account/internal/{identityId}/language")
                            {
                                Content = JsonContent.Create(new { language = lang })
                            };
                            httpReq.Headers.Add("X-Api-Key", apiKey);
                            await client.SendAsync(httpReq);
                        }
                        catch (Exception ex) { _logger.LogError(ex, "Erreur mise à jour langue Identity pour {Id}", identityId); }
                    });
                }
            }
            try
            {
                var updated = await _service.UpdateAsync(existing);
                return Ok(_builder.Build(updated));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur UpdateAsync utilisateur {Id}", id);
                return StatusCode(500, new { error = ex.Message, detail = ex.InnerException?.Message });
            }
        }

        [HttpPut("{id}/role")]
        [SwaggerOperation(Summary = "Change le rôle d'un utilisateur (admin only)")]
        public async Task<IActionResult> UpdateRole(int id, [FromBody] AdminUpdateRoleRequest req)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();
            if (string.IsNullOrEmpty(existing.IdentityUserId))
                return BadRequest(new { error = "Utilisateur sans compte Identity." });

            try
            {
                var client = _httpClientFactory.CreateClient("identity");
                var apiKey = _config["IdentityServer:InternalApiKey"] ?? "";
                var httpReq = new HttpRequestMessage(HttpMethod.Put, $"/api/auth/account/internal/{existing.IdentityUserId}/role")
                {
                    Content = JsonContent.Create(new { role = req.Role })
                };
                httpReq.Headers.Add("X-Api-Key", apiKey);
                var res = await client.SendAsync(httpReq);
                if (!res.IsSuccessStatusCode)
                    return StatusCode((int)res.StatusCode, new { error = "Erreur lors du changement de rôle." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur changement rôle utilisateur {Id}", id);
                return StatusCode(500, new { error = "Erreur lors du changement de rôle." });
            }

            existing.Role = req.Role;
            await _service.UpdateAsync(existing);

            return Ok();
        }

        [HttpPost("admin/create")]
        [SwaggerOperation(Summary = "Crée un utilisateur avec rôle (admin only)")]
        public async Task<IActionResult> AdminCreate([FromBody] AdminCreateRequest req)
        {
            string identityUserId;
            try
            {
                var client = _httpClientFactory.CreateClient("identity");
                var apiKey = _config["IdentityServer:InternalApiKey"] ?? "";
                var httpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/account/internal/create")
                {
                    Content = JsonContent.Create(new
                    {
                        email = req.Email,
                        password = req.Password,
                        prenom = req.Prenom,
                        nom = req.Nom,
                        role = req.Role,
                    })
                };
                httpReq.Headers.Add("X-Api-Key", apiKey);
                var res = await client.SendAsync(httpReq);
                if (!res.IsSuccessStatusCode)
                {
                    var err = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                    return BadRequest(new { error = err?.GetValueOrDefault("error") ?? "Erreur création compte." });
                }
                var created = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                identityUserId = created?["userId"] ?? throw new Exception("userId manquant");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur création compte Identity admin");
                return StatusCode(500, new { error = "Erreur lors de la création du compte." });
            }

            var utilisateur = new Utilisateur
            {
                IdentityUserId = identityUserId,
                Prenom = req.Prenom,
                Nom = req.Nom,
                Email = req.Email,
                Telephone = req.Telephone ?? "",
                RayonNotification = 5,
                Role = string.IsNullOrWhiteSpace(req.Role) ? "User" : req.Role,
            };
            var profil = await _service.CreateAsync(utilisateur);
            return CreatedAtAction(nameof(GetById), new { id = profil.Id }, _builder.Build(profil));
        }

        [HttpPut("{id}/import-flyer")]
        [SwaggerOperation(Summary = "Active ou désactive la permission d'import flyer pour un utilisateur")]
        public async Task<IActionResult> SetImportFlyer(int id, [FromBody] SetImportFlyerRequest req)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();
            existing.CanImportFlyer = req.CanImportFlyer;
            var updated = await _service.UpdateAsync(existing);

            // Notification silencieuse immédiate si l'utilisateur a un token Expo
            if (!string.IsNullOrEmpty(updated.ExpoToken))
            {
                _ = Task.Run(() => _push.SendPermissionUpdateAsync(updated.ExpoToken, updated.CanImportFlyer));
            }

            return Ok(_builder.Build(updated));
        }

        [HttpPut("import-flyer/bulk")]
        [SwaggerOperation(Summary = "Active ou désactive la permission d'import flyer pour tous les utilisateurs")]
        public async Task<IActionResult> SetImportFlyerBulk([FromBody] SetImportFlyerRequest req)
        {
            // Une seule requête SQL UPDATE — répond immédiatement
            int updated = await _service.BulkSetCanImportFlyerAsync(req.CanImportFlyer);

            // Envoi des notifs en arrière-plan : lit les tokens page par page (5 000 à la fois)
            // pour ne jamais charger 1M d'entrées en mémoire simultanément
            bool canImportFlyer = req.CanImportFlyer;
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IUtilisateurService>();
                var push = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

                const int PageSize = 5_000;
                int offset = 0;
                while (true)
                {
                    var page = await svc.GetExpoTokensPageAsync("User", offset, PageSize);
                    if (page.Count == 0) break;
                    await push.SendPermissionUpdateToManyAsync(page, canImportFlyer);
                    if (page.Count < PageSize) break;
                    offset += PageSize;
                }
            });

            return Ok(new { updated });
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(Summary = "Supprime un utilisateur")]
        public async Task<IActionResult> Delete(int id)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();

            var prenom = existing.Prenom ?? "";
            var email = existing.Email ?? "";
            var language = NormalizeLanguage(existing.Language);
            var identityUserId = existing.IdentityUserId;

            if (!string.IsNullOrEmpty(identityUserId))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient("identity");
                    var apiKey = _config["IdentityServer:InternalApiKey"] ?? "";
                    var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/auth/account/internal/{identityUserId}");
                    req.Headers.Add("X-Api-Key", apiKey);
                    var res = await client.SendAsync(req);
                    if (!res.IsSuccessStatusCode)
                        _logger.LogWarning("Suppression Identity échouée pour {Id}: {Status}", identityUserId, res.StatusCode);
                }
                catch (Exception ex) { _logger.LogError(ex, "Erreur suppression Identity pour {Id}", identityUserId); }
            }

            await _service.DeleteAsync(id);

            if (!string.IsNullOrEmpty(email))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var html = BuildDeleteAccountHtml(prenom, email, language);
                        var subject = language switch {
                            "en" => "Your Salat Janaza account has been deleted",
                            "ar" => "تم حذف حسابك في صلاة الجنازة",
                            _ => "Votre compte Salat Janaza a été supprimé"
                        };
                        var msg = new MimeMessage();
                        msg.Subject = subject;
                        msg.Body = BuildBody(html);
                        await _emailService.SendRawAsync(email, msg);
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Erreur envoi email suppression compte à {Email}", email); }
                });
            }

            return NoContent();
        }

        private static string LogoPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "acc1.png");

        private static string NormalizeLanguage(string? lang) => lang?.ToLower() switch {
            "fr" => "fr", "en" => "en", "ar" => "ar",
            "tr" => "tr", "ja" => "ja", "ko" => "ko",
            "ms" => "ms", "ur" => "ur", "id" => "id", "bn" => "bn", "ru" => "ru", "pt" => "pt", "de" => "de", "it" => "it", "es" => "es",
            _ => "en"
        };

        private static string EmailHeader(string subtitle) => $@"
  <div style='background:#3A6B4A;padding:20px 28px;'>
    <table cellpadding='0' cellspacing='0' border='0' width='100%'>
      <tr>
        <td width='56' valign='middle'>
          <img src='cid:logo' width='48' height='48' alt='' style='display:block;border-radius:8px;'/>
        </td>
        <td valign='middle' style='padding-left:14px;'>
          <div style='color:#fff;font-size:20px;font-weight:800;letter-spacing:0.3px;font-family:Georgia,serif;'>Salat Janaza</div>
          <div style='color:rgba(255,255,255,0.65);font-size:11px;letter-spacing:1.5px;text-transform:uppercase;margin-top:2px;'>{subtitle}</div>
        </td>
      </tr>
    </table>
  </div>";

        private static string EmailFooter(string language = "fr")
        {
            var text = language switch { "en" => "Questions?", "ar" => "أسئلة؟", _ => "Une question ?" };
            return $@"
  <div style='background:#3A6B4A;padding:18px 28px;text-align:center;'>
    <p style='color:rgba(255,255,255,0.9);font-size:13px;margin:0;'>
      {text} <a href='mailto:support@salatjanaza.org' style='color:#ffffff !important;font-weight:700;text-decoration:none;'>support@salatjanaza.org</a>
    </p>
  </div>";
        }

        private static MimeEntity BuildBody(string html)
        {
            var htmlPart = new TextPart("html") { Text = html };
            if (!System.IO.File.Exists(LogoPath)) return htmlPart;
            var image = new MimePart("image", "png")
            {
                Content = new MimeContent(System.IO.File.OpenRead(LogoPath)),
                ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
                ContentTransferEncoding = ContentEncoding.Base64,
                ContentId = "logo",
            };
            return new MultipartRelated { htmlPart, image };
        }

        private static string BuildDeleteAccountHtml(string prenom, string email, string language = "fr") => language switch {
            "en" => BuildDeleteAccountHtmlEn(prenom, email),
            "ar" => BuildDeleteAccountHtmlAr(prenom, email),
            _ => BuildDeleteAccountHtmlFr(prenom, email)
        };

        private static string BuildDeleteAccountHtmlFr(string prenom, string email) => $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>
  {EmailHeader("Suppression de compte")}
  <div style='background:#fff;padding:22px 28px;border-bottom:1px solid #e8f0e9;'>
    <p style='color:#222;font-size:15px;margin:0;'>As salamou 3alaykoum wa rahmatulLahi wa barakatuh <strong>{prenom}</strong>,</p>
  </div>
  <div style='background:#fff;padding:22px 28px;'>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0 0 16px;'>
      Nous vous confirmons que votre compte <strong style='color:#3A6B4A;'>Salat Janaza</strong> associé à l'adresse <strong>{email}</strong> a bien été supprimé.
    </p>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0 0 20px;'>
      Toutes vos données ont été définitivement effacées de nos serveurs : votre profil, vos déclarations et vos abonnements.
    </p>
    <div style='background:#f4f8f4;border-left:4px solid #3A6B4A;border-radius:0 8px 8px 0;padding:16px 18px;margin:0 0 20px;text-align:center;'>
      <p style='font-size:18px;color:#3A6B4A;font-family:serif;direction:rtl;line-height:2;margin:0 0 8px;'>جَزَاكَ ٱللَّٰهُ خَيْرًا</p>
      <p style='color:#555;font-size:13px;font-style:italic;margin:0;'>
        Qu'Allah vous récompense du bien pour avoir participé à cette initiative.<br>
        Que vos prières ne s'arrêtent pas là.
      </p>
    </div>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0;'>
      Si cette suppression est une erreur, n'hésitez pas à nous contacter.
    </p>
  </div>
  {EmailFooter("fr")}
</div>
</body></html>";

        private static string BuildDeleteAccountHtmlEn(string prenom, string email) => $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>
  {EmailHeader("Account deleted")}
  <div style='background:#fff;padding:22px 28px;border-bottom:1px solid #e8f0e9;'>
    <p style='color:#222;font-size:15px;margin:0;'>As-salamu alaykum wa rahmatulLahi wa barakatuh <strong>{prenom}</strong>,</p>
  </div>
  <div style='background:#fff;padding:22px 28px;'>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0 0 16px;'>
      We confirm that your <strong style='color:#3A6B4A;'>Salat Janaza</strong> account associated with <strong>{email}</strong> has been deleted.
    </p>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0 0 20px;'>
      All your data has been permanently removed from our servers: your profile, declarations, and subscriptions.
    </p>
    <div style='background:#f4f8f4;border-left:4px solid #3A6B4A;border-radius:0 8px 8px 0;padding:16px 18px;margin:0 0 20px;text-align:center;'>
      <p style='font-size:18px;color:#3A6B4A;font-family:serif;direction:rtl;line-height:2;margin:0 0 8px;'>جَزَاكَ ٱللَّٰهُ خَيْرًا</p>
      <p style='color:#555;font-size:13px;font-style:italic;margin:0;'>
        May Allah reward you with good for participating in this initiative.<br>
        May your prayers continue.
      </p>
    </div>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0;'>
      If this deletion was a mistake, please do not hesitate to contact us.
    </p>
  </div>
  {EmailFooter("en")}
</div>
</body></html>";

        private static string BuildDeleteAccountHtmlAr(string prenom, string email) => $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;direction:rtl;'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>
  {EmailHeader("حذف الحساب")}
  <div style='background:#fff;padding:22px 28px;border-bottom:1px solid #e8f0e9;'>
    <p style='color:#222;font-size:15px;margin:0;'>السلام عليكم ورحمة الله وبركاته <strong>{prenom}</strong>،</p>
  </div>
  <div style='background:#fff;padding:22px 28px;'>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0 0 16px;'>
      نؤكد لك أن حساب <strong style='color:#3A6B4A;'>صلاة الجنازة</strong> المرتبط بالبريد الإلكتروني <strong>{email}</strong> قد تم حذفه.
    </p>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0 0 20px;'>
      تم حذف جميع بياناتك نهائياً من خوادمنا: ملفك الشخصي وإعلاناتك واشتراكاتك.
    </p>
    <div style='background:#f4f8f4;border-right:4px solid #3A6B4A;border-radius:8px 0 0 8px;padding:16px 18px;margin:0 0 20px;text-align:center;'>
      <p style='font-size:18px;color:#3A6B4A;font-family:serif;line-height:2;margin:0 0 8px;'>جَزَاكَ ٱللَّٰهُ خَيْرًا</p>
      <p style='color:#555;font-size:13px;margin:0;'>جزاك الله خيراً على مشاركتك في هذه المبادرة.</p>
    </div>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0;'>
      إذا كان هذا الحذف خطأً، فلا تتردد في التواصل معنا.
    </p>
  </div>
  {EmailFooter("ar")}
</div>
</body></html>";
    }
}
