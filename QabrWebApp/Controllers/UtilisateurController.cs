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
                        var dl = _deleteStrings.TryGetValue(language, out var dlv) ? dlv : _deleteStrings["en"];
                        var subject = dl.Subject;
                        var html = BuildDeleteAccountHtml(prenom, email, language);
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
            "ms" => "ms", "ur" => "ur", "id" => "id", "bn" => "bn",
            "ru" => "ru", "pt" => "pt", "de" => "de", "it" => "it", "es" => "es",
            "bm" => "bm", "nl" => "nl",
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
            var text = language switch {
                "en" => "Questions?", "ar" => "أسئلة؟", "tr" => "Sorularınız mı var?",
                "de" => "Fragen?", "es" => "¿Preguntas?", "it" => "Domande?",
                "pt" => "Perguntas?", "ru" => "Вопросы?", "ja" => "ご質問は？",
                "ko" => "질문이 있으신가요?", "ms" => "Soalan?", "id" => "Pertanyaan?",
                "bn" => "প্রশ্ন আছে?", "ur" => "سوالات؟", "bm" => "Ɲinigaliw?",
                "nl" => "Vragen?", _ => "Une question ?"
            };
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

        private sealed record DeleteL10n(
            string Subject, string Subtitle, string Greeting,
            string Confirmed, string DataDeleted,
            string JazakAllah, string JazakAllahSub,
            string ErrorContact, bool Rtl = false);

        private static readonly Dictionary<string, DeleteL10n> _deleteStrings = new()
        {
            ["fr"] = new(
                Subject: "Votre compte Salat Janaza a été supprimé",
                Subtitle: "Suppression de compte",
                Greeting: "As salamou 3alaykoum wa rahmatulLahi wa barakatuh",
                Confirmed: "Nous vous confirmons que votre compte <strong style='color:#3A6B4A;'>Salat Janaza</strong> associé à l'adresse <strong>{email}</strong> a bien été supprimé.",
                DataDeleted: "Toutes vos données ont été définitivement effacées de nos serveurs : votre profil, vos déclarations et vos abonnements.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "Qu'Allah vous récompense du bien pour avoir participé à cette initiative.<br>Que vos prières ne s'arrêtent pas là.",
                ErrorContact: "Si cette suppression est une erreur, n'hésitez pas à nous contacter."),
            ["en"] = new(
                Subject: "Your Salat Janaza account has been deleted",
                Subtitle: "Account deleted",
                Greeting: "As-salamu alaykum wa rahmatulLahi wa barakatuh",
                Confirmed: "We confirm that your <strong style='color:#3A6B4A;'>Salat Janaza</strong> account associated with <strong>{email}</strong> has been deleted.",
                DataDeleted: "All your data has been permanently removed from our servers: your profile, declarations, and subscriptions.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "May Allah reward you with good for participating in this initiative.<br>May your prayers continue.",
                ErrorContact: "If this deletion was a mistake, please do not hesitate to contact us."),
            ["ar"] = new(
                Subject: "تم حذف حسابك في صلاة الجنازة",
                Subtitle: "حذف الحساب",
                Greeting: "السلام عليكم ورحمة الله وبركاته",
                Confirmed: "نؤكد لك أن حساب <strong style='color:#3A6B4A;'>صلاة الجنازة</strong> المرتبط بالبريد الإلكتروني <strong>{email}</strong> قد تم حذفه.",
                DataDeleted: "تم حذف جميع بياناتك نهائياً من خوادمنا: ملفك الشخصي وإعلاناتك واشتراكاتك.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "جزاك الله خيراً على مشاركتك في هذه المبادرة.",
                ErrorContact: "إذا كان هذا الحذف خطأً، فلا تتردد في التواصل معنا.",
                Rtl: true),
            ["tr"] = new(
                Subject: "Salat Janaza hesabınız silindi",
                Subtitle: "Hesap silindi",
                Greeting: "Es-selâmu aleyküm ve rahmetullahi ve berakâtüh",
                Confirmed: "<strong style='color:#3A6B4A;'>Salat Janaza</strong> hesabınızın <strong>{email}</strong> adresiyle ilişkili olarak silindiğini onaylıyoruz.",
                DataDeleted: "Tüm verileriniz sunucularımızdan kalıcı olarak silindi: profiliniz, bildirimleriniz ve abonelikleriniz.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "Bu girişime katıldığınız için Allah sizi hayırla mükâfatlandırsın.<br>Dualarınız devam etsin.",
                ErrorContact: "Bu silme işlemi bir hata ise, bizimle iletişime geçmekten çekinmeyin."),
            ["de"] = new(
                Subject: "Ihr Salat Janaza-Konto wurde gelöscht",
                Subtitle: "Konto gelöscht",
                Greeting: "As-salamu alaikum wa rahmatullahi wa barakatuh",
                Confirmed: "Wir bestätigen, dass Ihr <strong style='color:#3A6B4A;'>Salat Janaza</strong>-Konto mit der E-Mail-Adresse <strong>{email}</strong> gelöscht wurde.",
                DataDeleted: "Alle Ihre Daten wurden dauerhaft von unseren Servern entfernt: Ihr Profil, Ihre Meldungen und Abonnements.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "Möge Allah Sie mit Gutem belohnen für Ihre Teilnahme an dieser Initiative.<br>Mögen Ihre Gebete weitergehen.",
                ErrorContact: "Wenn diese Löschung ein Fehler war, zögern Sie nicht, uns zu kontaktieren."),
            ["es"] = new(
                Subject: "Tu cuenta de Salat Janaza ha sido eliminada",
                Subtitle: "Cuenta eliminada",
                Greeting: "As-salamu alaykum wa rahmatullahi wa barakatuh",
                Confirmed: "Confirmamos que tu cuenta de <strong style='color:#3A6B4A;'>Salat Janaza</strong> asociada a <strong>{email}</strong> ha sido eliminada.",
                DataDeleted: "Todos tus datos han sido eliminados permanentemente de nuestros servidores: tu perfil, declaraciones y suscripciones.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "Que Allah te recompense con el bien por participar en esta iniciativa.<br>Que tus oraciones continúen.",
                ErrorContact: "Si esta eliminación fue un error, no dudes en contactarnos."),
            ["it"] = new(
                Subject: "Il tuo account Salat Janaza è stato eliminato",
                Subtitle: "Account eliminato",
                Greeting: "As-salamu alaykum wa rahmatullahi wa barakatuh",
                Confirmed: "Confermiamo che il tuo account <strong style='color:#3A6B4A;'>Salat Janaza</strong> associato a <strong>{email}</strong> è stato eliminato.",
                DataDeleted: "Tutti i tuoi dati sono stati rimossi definitivamente dai nostri server: il tuo profilo, le dichiarazioni e le iscrizioni.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "Che Allah ti ricompensi con il bene per aver partecipato a questa iniziativa.<br>Che le tue preghiere continuino.",
                ErrorContact: "Se questa eliminazione è stata un errore, non esitare a contattarci."),
            ["pt"] = new(
                Subject: "Sua conta Salat Janaza foi excluída",
                Subtitle: "Conta excluída",
                Greeting: "As-salamu alaykum wa rahmatullahi wa barakatuh",
                Confirmed: "Confirmamos que sua conta <strong style='color:#3A6B4A;'>Salat Janaza</strong> associada a <strong>{email}</strong> foi excluída.",
                DataDeleted: "Todos os seus dados foram removidos permanentemente de nossos servidores: seu perfil, declarações e assinaturas.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "Que Allah te recompense com o bem por participar desta iniciativa.<br>Que suas orações continuem.",
                ErrorContact: "Se esta exclusão foi um erro, não hesite em nos contatar."),
            ["ru"] = new(
                Subject: "Ваш аккаунт Salat Janaza был удалён",
                Subtitle: "Аккаунт удалён",
                Greeting: "Ас-саляму алейкум ва рахматуллахи ва баракатух",
                Confirmed: "Мы подтверждаем, что ваш аккаунт <strong style='color:#3A6B4A;'>Salat Janaza</strong>, связанный с адресом <strong>{email}</strong>, был удалён.",
                DataDeleted: "Все ваши данные были безвозвратно удалены с наших серверов: ваш профиль, объявления и подписки.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "Да вознаградит вас Аллах добром за участие в этой инициативе.<br>Пусть ваши молитвы не прекращаются.",
                ErrorContact: "Если удаление было ошибкой, пожалуйста, свяжитесь с нами."),
            ["ja"] = new(
                Subject: "Salat Janazaアカウントが削除されました",
                Subtitle: "アカウント削除",
                Greeting: "アッサラーム・アライクム・ワ・ラフマトゥッラーヒ・ワ・バラカートゥフ",
                Confirmed: "<strong>{email}</strong>に関連付けられた<strong style='color:#3A6B4A;'>Salat Janaza</strong>アカウントが削除されたことをお知らせします。",
                DataDeleted: "プロフィール、申告、サブスクリプションを含むすべてのデータがサーバーから完全に削除されました。",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "この活動にご参加いただいたことに対し、アッラーが善い報酬を与えてくださいますように。<br>あなたのお祈りが続きますように。",
                ErrorContact: "この削除が間違いの場合は、お気軽にお問い合わせください。"),
            ["ko"] = new(
                Subject: "Salat Janaza 계정이 삭제되었습니다",
                Subtitle: "계정 삭제",
                Greeting: "앗살라무 알라이쿰 와 라흐마툴라히 와 바라카투후",
                Confirmed: "<strong>{email}</strong>와 연결된 <strong style='color:#3A6B4A;'>Salat Janaza</strong> 계정이 삭제되었음을 확인합니다.",
                DataDeleted: "귀하의 모든 데이터(프로필, 신고, 구독)가 서버에서 영구적으로 삭제되었습니다.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "이 활동에 참여해 주셔서 알라께서 선한 보상을 내려주시길 바랍니다.<br>기도가 계속되길 바랍니다.",
                ErrorContact: "이 삭제가 실수인 경우 주저하지 말고 저희에게 연락해 주세요."),
            ["ms"] = new(
                Subject: "Akaun Salat Janaza anda telah dipadam",
                Subtitle: "Akaun dipadam",
                Greeting: "Assalamualaikum warahmatullahi wabarakatuh",
                Confirmed: "Kami mengesahkan bahawa akaun <strong style='color:#3A6B4A;'>Salat Janaza</strong> anda yang dikaitkan dengan <strong>{email}</strong> telah dipadam.",
                DataDeleted: "Semua data anda telah dipadamkan secara kekal dari pelayan kami: profil, pengisytiharan dan langganan anda.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "Semoga Allah membalas anda dengan kebaikan kerana menyertai inisiatif ini.<br>Semoga doa-doa anda berterusan.",
                ErrorContact: "Jika pemadaman ini adalah satu kesilapan, jangan teragak-agak untuk menghubungi kami."),
            ["id"] = new(
                Subject: "Akun Salat Janaza Anda telah dihapus",
                Subtitle: "Akun dihapus",
                Greeting: "Assalamu'alaikum warahmatullahi wabarakatuh",
                Confirmed: "Kami mengkonfirmasi bahwa akun <strong style='color:#3A6B4A;'>Salat Janaza</strong> Anda yang terkait dengan <strong>{email}</strong> telah dihapus.",
                DataDeleted: "Semua data Anda telah dihapus secara permanen dari server kami: profil, deklarasi, dan langganan Anda.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "Semoga Allah membalas Anda dengan kebaikan atas keikutsertaan dalam inisiatif ini.<br>Semoga doa-doa Anda terus berlanjut.",
                ErrorContact: "Jika penghapusan ini adalah kesalahan, jangan ragu untuk menghubungi kami."),
            ["bn"] = new(
                Subject: "আপনার Salat Janaza অ্যাকাউন্ট মুছে ফেলা হয়েছে",
                Subtitle: "অ্যাকাউন্ট মুছে ফেলা হয়েছে",
                Greeting: "আস্‌সালামু আলাইকুম ওয়া রাহমাতুল্লাহি ওয়া বারাকাতুহ",
                Confirmed: "আমরা নিশ্চিত করছি যে <strong>{email}</strong>-এর সাথে যুক্ত আপনার <strong style='color:#3A6B4A;'>Salat Janaza</strong> অ্যাকাউন্ট মুছে ফেলা হয়েছে।",
                DataDeleted: "আপনার সমস্ত ডেটা আমাদের সার্ভার থেকে স্থায়ীভাবে মুছে ফেলা হয়েছে: আপনার প্রোফাইল, ঘোষণা এবং সাবস্ক্রিপশন।",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "এই উদ্যোগে অংশগ্রহণের জন্য আল্লাহ আপনাকে উত্তম পুরস্কার দিন।<br>আপনার দোয়া অব্যাহত থাকুক।",
                ErrorContact: "যদি এই মুছে ফেলা একটি ভুল হয়, তাহলে আমাদের সাথে যোগাযোগ করতে দ্বিধা করবেন না।"),
            ["ur"] = new(
                Subject: "آپ کا Salat Janaza اکاؤنٹ حذف کر دیا گیا ہے",
                Subtitle: "اکاؤنٹ حذف",
                Greeting: "السلام علیکم ورحمۃ اللہ وبرکاتہ",
                Confirmed: "ہم تصدیق کرتے ہیں کہ <strong>{email}</strong> سے منسلک آپ کا <strong style='color:#3A6B4A;'>Salat Janaza</strong> اکاؤنٹ حذف کر دیا گیا ہے۔",
                DataDeleted: "آپ کا تمام ڈیٹا ہمارے سرورز سے مستقل طور پر حذف کر دیا گیا ہے: آپ کا پروفائل، اعلانات اور سبسکرپشنز۔",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "اللہ آپ کو اس اقدام میں حصہ لینے پر بھلائی سے نوازے۔<br>آپ کی دعائیں جاری رہیں۔",
                ErrorContact: "اگر یہ حذف ایک غلطی تھی، تو ہم سے رابطہ کرنے میں ہچکچاہٹ نہ کریں۔",
                Rtl: true),
            ["bm"] = new(
                Subject: "I ka compte Salat Janaza jɛnsɛnna",
                Subtitle: "Compte jɛnsɛnna",
                Greeting: "I ni ce, Ala k'aw sariya",
                Confirmed: "An b'a ɲɛfɔ ko i ka <strong style='color:#3A6B4A;'>Salat Janaza</strong> compte min tun jɛlen don <strong>{email}</strong> ma, o jɛnsɛnna.",
                DataDeleted: "I ka kunnafoni bɛɛ banna an ka serveur kan: i ka profil, i ka sɔsɔtaw ani i ka abonnement.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "Ala k'i sara ko i donna nin laɲinin na.<br>I ka seliw kelen tun.",
                ErrorContact: "N'o kɛra fili ye, i ka an kulelen."),
            ["nl"] = new(
                Subject: "Uw Salat Janaza-account is verwijderd",
                Subtitle: "Account verwijderd",
                Greeting: "As-salamu alaykum wa rahmatullahi wa barakatuh",
                Confirmed: "Wij bevestigen dat uw <strong style='color:#3A6B4A;'>Salat Janaza</strong>-account gekoppeld aan <strong>{email}</strong> is verwijderd.",
                DataDeleted: "Al uw gegevens zijn permanent verwijderd van onze servers: uw profiel, declaraties en abonnementen.",
                JazakAllah: "جَزَاكَ ٱللَّٰهُ خَيْرًا",
                JazakAllahSub: "Moge Allah u belonen met het goede voor uw deelname aan dit initiatief.<br>Moge uw gebeden doorgaan.",
                ErrorContact: "Als deze verwijdering een vergissing was, aarzel dan niet om contact met ons op te nemen."),
        };

        private static string BuildDeleteAccountHtml(string prenom, string userEmail, string lang = "fr")
        {
            var l = _deleteStrings.TryGetValue(lang, out var v) ? v : _deleteStrings["en"];
            var dir = l.Rtl ? "direction:rtl;" : "";
            var borderSide = l.Rtl ? "border-right" : "border-left";
            var borderRadius = l.Rtl ? "8px 0 0 8px" : "0 8px 8px 0";
            var confirmed = l.Confirmed.Replace("{email}", userEmail);
            return $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;{dir}'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>
  {EmailHeader(l.Subtitle)}
  <div style='background:#fff;padding:22px 28px;border-bottom:1px solid #e8f0e9;'>
    <p style='color:#222;font-size:15px;margin:0;'>{l.Greeting} <strong>{prenom}</strong>,</p>
  </div>
  <div style='background:#fff;padding:22px 28px;'>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0 0 16px;'>{confirmed}</p>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0 0 20px;'>{l.DataDeleted}</p>
    <div style='background:#f4f8f4;{borderSide}:4px solid #3A6B4A;border-radius:{borderRadius};padding:16px 18px;margin:0 0 20px;text-align:center;'>
      <p style='font-size:18px;color:#3A6B4A;font-family:serif;direction:rtl;line-height:2;margin:0 0 8px;'>{l.JazakAllah}</p>
      <p style='color:#555;font-size:13px;font-style:italic;margin:0;'>{l.JazakAllahSub}</p>
    </div>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0;'>{l.ErrorContact}</p>
  </div>
  {EmailFooter(lang)}
</div>
</body></html>";
        }
    }
}
