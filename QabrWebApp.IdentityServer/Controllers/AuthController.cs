using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using MimeKit.Utils;
using QabrWebApp.IdentityServer.Models;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;

namespace QabrWebApp.IdentityServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthController> _logger;

        private static readonly ConcurrentDictionary<string, (string Code, string Token, DateTime Expiry)> _resetCodes = new();

        public AuthController(UserManager<ApplicationUser> userManager, IConfiguration config, ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _config = config;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req)
        {
            var lang = NormalizeLanguage(req.Language);
            var user = new ApplicationUser
            {
                UserName = req.Email,
                Email = req.Email,
                Prenom = req.Prenom,
                Nom = req.Nom,
                EmailConfirmed = true,
                Language = lang,
            };

            var result = await _userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

            _ = Task.Run(async () =>
            {
                try { await SendWelcomeEmailAsync(req.Email, req.Prenom, req.Nom, lang); }
                catch (Exception ex) { _logger.LogError(ex, "Erreur envoi email bienvenue à {Email}", req.Email); }
            });

            return Ok(new { userId = user.Id, email = user.Email, message = "Compte créé avec succès." });
        }

        [HttpDelete("account/internal/{identityUserId}")]
        public async Task<IActionResult> DeleteAccountInternal(string identityUserId)
        {
            var expectedKey = _config["InternalApiKey"];
            var providedKey = Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(expectedKey) || providedKey != expectedKey)
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(identityUserId);
            if (user is null) return NoContent();

            await _userManager.DeleteAsync(user);
            return NoContent();
        }

        [HttpPut("account/internal/{identityUserId}/role")]
        public async Task<IActionResult> UpdateRoleInternal(string identityUserId, [FromBody] UpdateRoleRequest req)
        {
            var expectedKey = _config["InternalApiKey"];
            var providedKey = Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(expectedKey) || providedKey != expectedKey)
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(identityUserId);
            if (user is null) return NotFound();

            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);

            var roleManager = HttpContext.RequestServices.GetRequiredService<RoleManager<IdentityRole>>();
            var newRole = string.IsNullOrWhiteSpace(req.Role) ? "User" : req.Role;
            if (!await roleManager.RoleExistsAsync(newRole))
                await roleManager.CreateAsync(new IdentityRole(newRole));
            await _userManager.AddToRoleAsync(user, newRole);

            return Ok();
        }

        [HttpPut("account/internal/{identityUserId}/language")]
        public async Task<IActionResult> UpdateLanguageInternal(string identityUserId, [FromBody] UpdateLanguageRequest req)
        {
            var expectedKey = _config["InternalApiKey"];
            var providedKey = Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(expectedKey) || providedKey != expectedKey)
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(identityUserId);
            if (user is null) return NotFound();
            user.Language = NormalizeLanguage(req.Language);
            await _userManager.UpdateAsync(user);
            return Ok();
        }

        [HttpPost("account/internal/create")]
        public async Task<IActionResult> CreateInternal([FromBody] InternalCreateRequest req)
        {
            var expectedKey = _config["InternalApiKey"];
            var providedKey = Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(expectedKey) || providedKey != expectedKey)
                return Unauthorized();

            var existing = await _userManager.FindByEmailAsync(req.Email);
            if (existing is not null)
                return BadRequest(new { error = "Un compte avec cet email existe déjà." });

            var user = new ApplicationUser
            {
                UserName = req.Email,
                Email = req.Email,
                Prenom = req.Prenom,
                Nom = req.Nom,
                EmailConfirmed = true,
            };

            var result = await _userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded)
            {
                var error = result.Errors.FirstOrDefault()?.Description ?? "Erreur lors de la création.";
                return BadRequest(new { error });
            }

            var roleManager = HttpContext.RequestServices.GetRequiredService<RoleManager<IdentityRole>>();
            var role = string.IsNullOrWhiteSpace(req.Role) ? "User" : req.Role;
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
            await _userManager.AddToRoleAsync(user, role);

            return Ok(new { userId = user.Id, email = user.Email });
        }

        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            if (User.Identity?.IsAuthenticated != true) return Unauthorized();
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return NotFound();
            return Ok(new { user.Id, user.Email, user.Prenom, user.Nom });
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
        {
            var user = await _userManager.FindByEmailAsync(req.Email);
            if (user is null) return BadRequest(new { error = "Utilisateur introuvable." });

            var result = await _userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
            if (!result.Succeeded)
            {
                var error = result.Errors.FirstOrDefault()?.Description ?? "Erreur lors du changement de mot de passe.";
                return BadRequest(new { error });
            }

            return Ok(new { message = "Mot de passe modifié avec succès." });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
        {
            var user = await _userManager.FindByEmailAsync(req.Email);
            if (user is null)
                return Ok(new { message = "Si ce compte existe, un code a été envoyé par email." });

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var code = new Random().Next(100000, 999999).ToString();
            _resetCodes[req.Email.ToLower()] = (code, token, DateTime.UtcNow.AddMinutes(15));

            var resetLang = NormalizeLanguage(!string.IsNullOrWhiteSpace(req.Language) ? req.Language : user.Language);
            try
            {
                await SendResetEmailAsync(req.Email, code, resetLang);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur envoi email réinitialisation à {Email}", req.Email);
                return StatusCode(500, new { error = "Erreur lors de l'envoi de l'email." });
            }

            return Ok(new { message = "Si ce compte existe, un code a été envoyé par email." });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
        {
            var key = req.Email.ToLower();
            if (!_resetCodes.TryGetValue(key, out var entry))
                return BadRequest(new { error = "Code invalide ou expiré." });

            if (entry.Expiry < DateTime.UtcNow)
            {
                _resetCodes.TryRemove(key, out _);
                return BadRequest(new { error = "Code expiré. Faites une nouvelle demande." });
            }

            if (entry.Code != req.Code)
                return BadRequest(new { error = "Code incorrect." });

            var user = await _userManager.FindByEmailAsync(req.Email);
            if (user is null) return BadRequest(new { error = "Utilisateur introuvable." });

            var result = await _userManager.ResetPasswordAsync(user, entry.Token, req.NewPassword);
            _resetCodes.TryRemove(key, out _);

            if (!result.Succeeded)
            {
                var error = result.Errors.FirstOrDefault()?.Description ?? "Erreur lors de la réinitialisation.";
                return BadRequest(new { error });
            }

            return Ok(new { message = "Mot de passe réinitialisé avec succès." });
        }

        private static MimeMessage BuildBaseMessage(IConfiguration config, string recipientEmail, string subject)
        {
            var settings = config.GetSection("EmailSettings");
            var msg = new MimeMessage();
            msg.From.Add(MailboxAddress.Parse(settings["SenderEmail"]!));
            msg.To.Add(MailboxAddress.Parse(recipientEmail));
            msg.Subject = subject;
            return msg;
        }

        private static MimeEntity BuildBody(string html, string logoPath)
        {
            var htmlPart = new TextPart("html") { Text = html };
            if (!System.IO.File.Exists(logoPath)) return htmlPart;

            var image = new MimePart("image", "png")
            {
                Content = new MimeContent(System.IO.File.OpenRead(logoPath)),
                ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
                ContentTransferEncoding = ContentEncoding.Base64,
                ContentId = "logo",
            };
            return new MultipartRelated { htmlPart, image };
        }

        private static string LogoPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "acc1.png");

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

        private static string NormalizeLanguage(string? lang) => lang?.ToLower() switch {
            "fr" => "fr", "en" => "en", "ar" => "ar", _ => "en"
        };

        private async Task SendEmailAsync(MimeMessage message)
        {
            var settings = _config.GetSection("EmailSettings");
            using var client = new SmtpClient();
            await client.ConnectAsync(settings["SmtpHost"]!, int.Parse(settings["SmtpPort"]!), SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(settings["SenderEmail"]!, settings["SenderPassword"]!);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        private async Task SendWelcomeEmailAsync(string recipientEmail, string prenom, string nom, string language = "fr")
        {
            string subject = language switch {
                "en" => "Welcome to Salat Janaza",
                "ar" => "مرحباً بك في صلاة الجنازة",
                _ => "Bienvenue sur Salat Janaza"
            };
            var message = BuildBaseMessage(_config, recipientEmail, subject);
            var html = language switch {
                "en" => BuildWelcomeHtmlEn(prenom, nom, recipientEmail),
                "ar" => BuildWelcomeHtmlAr(prenom, nom, recipientEmail),
                _ => BuildWelcomeHtmlFr(prenom, nom, recipientEmail)
            };
            message.Body = BuildBody(html, LogoPath);
            await SendEmailAsync(message);
        }

        private string BuildWelcomeHtmlFr(string prenom, string nom, string email) => $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>
  {EmailHeader("Bienvenue")}
  <div style='background:#fff;padding:24px 28px;border-bottom:1px solid #e8f0e9;'>
    <p style='color:#222;font-size:15px;margin:0 0 8px;'>As salamou 3alaykoum wa rahmatulLahi wa barakatuh <strong>{prenom}</strong>,</p>
    <p style='color:#222;font-size:16px;margin:0;'>Bienvenue sur <strong style='color:#3A6B4A;'>Salat Janaza</strong>&nbsp;!</p>
  </div>
  <div style='background:#fff;padding:20px 28px;'>
    <p style='color:#555;font-size:14px;line-height:1.7;margin:0 0 16px;'>Votre compte a bien été créé. Voici un récapitulatif de vos informations :</p>
    <div style='background:#f4f8f4;border-left:4px solid #3A6B4A;border-radius:0 8px 8px 0;padding:14px 18px;'>
      <p style='margin:0 0 6px;font-size:14px;color:#444;'><strong>Prénom :</strong> {prenom}</p>
      <p style='margin:0 0 6px;font-size:14px;color:#444;'><strong>Nom :</strong> {nom}</p>
      <p style='margin:0;font-size:14px;color:#444;'><strong>Email :</strong> {email}</p>
    </div>
  </div>
  <div style='background:#f9fcf9;padding:20px 28px;border-top:1px solid #e8f0e9;border-bottom:1px solid #e8f0e9;'>
    <h2 style='color:#3A6B4A;font-size:16px;margin:0 0 10px;'>Notre mission</h2>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0;'>
      <strong>Salat Janaza</strong> a pour but de permettre aux défunts d'avoir le plus de monde possible lors de leur prière mortuaire, et de permettre aux croyants de ne pas manquer les immenses récompenses liées à la <em>Salat al-Janaza</em>.
    </p>
  </div>
  <div style='background:#fff;padding:20px 28px;'>
    <h2 style='color:#3A6B4A;font-size:16px;margin:0 0 14px;'>Les mérites de la Salat al-Janaza</h2>
    <blockquote style='border-left:4px solid #3A6B4A;padding-left:14px;margin:0 0 12px;color:#444;font-style:italic;font-size:14px;line-height:1.8;'>
      Notre Prophète (ﷺ) a dit : « Celui qui suit le convoi funèbre du musulman, poussé par sa foi et désirant la rétribution de Dieu [auprès de Lui] jusqu'à ce qu'on prie sur le mort, aura comme récompense le poids d'un qirât, et celui qui reste jusqu'à ce qu'on l'enterre aura comme récompense le poids de deux qirât. »
    </blockquote>
    <p style='color:#555;font-size:14px;line-height:1.7;margin:0 0 10px;'>On lui demanda alors : « Ô Messager de Dieu, que sont les deux qirât ? »</p>
    <blockquote style='border-left:4px solid #3A6B4A;padding-left:14px;margin:0 0 10px;color:#444;font-style:italic;font-size:14px;line-height:1.8;'>
      Il dit : « C'est l'équivalent [du poids] de deux grandes montagnes. »
    </blockquote>
    <p style='color:#999;font-size:12px;font-style:italic;margin:0 0 12px;'>(Bukhârî 1239, Muslim 1570, les quatre sunans et Ahmad 8841)</p>
    <p style='color:#555;font-size:14px;line-height:1.7;margin:0 0 14px;font-style:italic;'>Et dans une autre version : « Le plus petit des qirât pèsera le poids de [la montagne] Uhud. »</p>
    <p style='color:#3A6B4A;font-size:14px;font-weight:600;margin:0;'>Qu'Allah nous accorde la sincérité dans nos actions.</p>
  </div>
  {EmailFooter("fr")}
</div>
</body></html>";

        private string BuildWelcomeHtmlEn(string prenom, string nom, string email) => $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>
  {EmailHeader("Welcome")}
  <div style='background:#fff;padding:24px 28px;border-bottom:1px solid #e8f0e9;'>
    <p style='color:#222;font-size:15px;margin:0 0 8px;'>As-salamu alaykum wa rahmatulLahi wa barakatuh <strong>{prenom}</strong>,</p>
    <p style='color:#222;font-size:16px;margin:0;'>Welcome to <strong style='color:#3A6B4A;'>Salat Janaza</strong>!</p>
  </div>
  <div style='background:#fff;padding:20px 28px;'>
    <p style='color:#555;font-size:14px;line-height:1.7;margin:0 0 16px;'>Your account has been created successfully. Here is a summary of your information:</p>
    <div style='background:#f4f8f4;border-left:4px solid #3A6B4A;border-radius:0 8px 8px 0;padding:14px 18px;'>
      <p style='margin:0 0 6px;font-size:14px;color:#444;'><strong>First name:</strong> {prenom}</p>
      <p style='margin:0 0 6px;font-size:14px;color:#444;'><strong>Last name:</strong> {nom}</p>
      <p style='margin:0;font-size:14px;color:#444;'><strong>Email:</strong> {email}</p>
    </div>
  </div>
  <div style='background:#f9fcf9;padding:20px 28px;border-top:1px solid #e8f0e9;border-bottom:1px solid #e8f0e9;'>
    <h2 style='color:#3A6B4A;font-size:16px;margin:0 0 10px;'>Our mission</h2>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0;'>
      <strong>Salat Janaza</strong> aims to help the deceased have as many people as possible at their funeral prayer, and to allow believers not to miss the immense rewards of <em>Salat al-Janaza</em>.
    </p>
  </div>
  <div style='background:#fff;padding:20px 28px;'>
    <h2 style='color:#3A6B4A;font-size:16px;margin:0 0 14px;'>The merits of Salat al-Janaza</h2>
    <blockquote style='border-left:4px solid #3A6B4A;padding-left:14px;margin:0 0 12px;color:#444;font-style:italic;font-size:14px;line-height:1.8;'>
      Our Prophet (ﷺ) said: ""Whoever follows the funeral procession of a Muslim, driven by faith and hoping for reward from Allah, until the prayer is offered over the deceased, will receive a reward equal to one qirat; and whoever stays until the burial will receive two qirats.""
    </blockquote>
    <p style='color:#555;font-size:14px;line-height:1.7;margin:0 0 10px;'>He was then asked: ""O Messenger of Allah, what are the two qirats?""</p>
    <blockquote style='border-left:4px solid #3A6B4A;padding-left:14px;margin:0 0 10px;color:#444;font-style:italic;font-size:14px;line-height:1.8;'>
      He said: ""They are like two great mountains.""
    </blockquote>
    <p style='color:#999;font-size:12px;font-style:italic;margin:0 0 12px;'>(Bukhârî 1239, Muslim 1570, the four sunans and Ahmad 8841)</p>
    <p style='color:#3A6B4A;font-size:14px;font-weight:600;margin:0;'>May Allah grant us sincerity in our actions.</p>
  </div>
  {EmailFooter("en")}
</div>
</body></html>";

        private string BuildWelcomeHtmlAr(string prenom, string nom, string email) => $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;direction:rtl;'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>
  {EmailHeader("أهلاً وسهلاً")}
  <div style='background:#fff;padding:24px 28px;border-bottom:1px solid #e8f0e9;'>
    <p style='color:#222;font-size:15px;margin:0 0 8px;'>السلام عليكم ورحمة الله وبركاته <strong>{prenom}</strong>،</p>
    <p style='color:#222;font-size:16px;margin:0;'>أهلاً وسهلاً بك في <strong style='color:#3A6B4A;'>صلاة الجنازة</strong>!</p>
  </div>
  <div style='background:#fff;padding:20px 28px;'>
    <p style='color:#555;font-size:14px;line-height:1.7;margin:0 0 16px;'>تم إنشاء حسابك بنجاح. إليك ملخص معلوماتك:</p>
    <div style='background:#f4f8f4;border-right:4px solid #3A6B4A;border-radius:8px 0 0 8px;padding:14px 18px;'>
      <p style='margin:0 0 6px;font-size:14px;color:#444;'><strong>الاسم الأول:</strong> {prenom}</p>
      <p style='margin:0 0 6px;font-size:14px;color:#444;'><strong>اللقب:</strong> {nom}</p>
      <p style='margin:0;font-size:14px;color:#444;'><strong>البريد الإلكتروني:</strong> {email}</p>
    </div>
  </div>
  <div style='background:#f9fcf9;padding:20px 28px;border-top:1px solid #e8f0e9;border-bottom:1px solid #e8f0e9;'>
    <h2 style='color:#3A6B4A;font-size:16px;margin:0 0 10px;'>رسالتنا</h2>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0;'>
      تهدف <strong>صلاة الجنازة</strong> إلى مساعدة المتوفى على الحصول على أكبر عدد ممكن من المصلين في صلاة جنازته، وتمكين المؤمنين من عدم تفويت الأجر العظيم لصلاة الجنازة.
    </p>
  </div>
  <div style='background:#fff;padding:20px 28px;'>
    <h2 style='color:#3A6B4A;font-size:16px;margin:0 0 14px;'>فضل صلاة الجنازة</h2>
    <blockquote style='border-right:4px solid #3A6B4A;padding-right:14px;margin:0 0 12px;color:#444;font-size:14px;line-height:2;'>
      قال النبي ﷺ: «مَنِ اتَّبَعَ جَنَازَةَ مُسْلِمٍ إِيمَاناً وَاحْتِسَاباً، وَكَانَ مَعَهُ حَتَّى يُصَلَّى عَلَيْهَا وَيُفْرَغَ مِنْ دَفْنِهَا، فَإِنَّهُ يَرْجِعُ مِنَ الأَجْرِ بِقِيرَاطَيْنِ، كُلُّ قِيرَاطٍ مِثْلُ أُحُدٍ، وَمَنْ صَلَّى عَلَيْهَا ثُمَّ رَجَعَ قَبْلَ أَنْ تُدْفَنَ، فَإِنَّهُ يَرْجِعُ بِقِيرَاطٍ»
    </blockquote>
    <p style='color:#999;font-size:12px;margin:0 0 12px;direction:ltr;text-align:right;'>(البخاري ١٢٣٩، مسلم ١٥٧٠، السنن الأربعة وأحمد ٨٨٤١)</p>
    <p style='color:#3A6B4A;font-size:14px;font-weight:600;margin:0;'>جعلنا الله وإياكم من المخلصين في أعمالنا.</p>
  </div>
  {EmailFooter("ar")}
</div>
</body></html>";

        private async Task SendResetEmailAsync(string recipientEmail, string code, string language = "fr")
        {
            string subject = language switch {
                "en" => "Reset your Salat Janaza password",
                "ar" => "إعادة تعيين كلمة المرور — صلاة الجنازة",
                _ => "Réinitialisation de votre mot de passe — Salat Janaza"
            };
            var message = BuildBaseMessage(_config, recipientEmail, subject);
            var html = language switch {
                "en" => BuildResetHtmlEn(code),
                "ar" => BuildResetHtmlAr(code),
                _ => BuildResetHtmlFr(code)
            };
            message.Body = BuildBody(html, LogoPath);
            await SendEmailAsync(message);
        }

        private string BuildResetHtmlFr(string code) => $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>
  {EmailHeader("Réinitialisation du mot de passe")}
  <div style='background:#fff;padding:28px;'>
    <p style='color:#222;font-size:15px;font-weight:600;margin:0 0 10px;'>Votre code de réinitialisation</p>
    <p style='color:#555;font-size:14px;line-height:1.7;margin:0 0 24px;'>
      Utilisez le code ci-dessous pour réinitialiser votre mot de passe. Il est valable <strong>15 minutes</strong>.
    </p>
    <div style='background:#f4f8f4;border:2px solid #3A6B4A;border-radius:12px;padding:26px 16px;text-align:center;margin:0 0 24px;'>
      <p style='font-size:46px;font-weight:900;letter-spacing:12px;color:#3A6B4A;margin:0;font-family:monospace;'>{code}</p>
    </div>
    <p style='color:#999;font-size:13px;line-height:1.6;margin:0;'>
      Si vous n'avez pas demandé cette réinitialisation, ignorez simplement cet email. Votre mot de passe ne sera pas modifié.
    </p>
  </div>
  {EmailFooter("fr")}
</div>
</body></html>";

        private string BuildResetHtmlEn(string code) => $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>
  {EmailHeader("Password reset")}
  <div style='background:#fff;padding:28px;'>
    <p style='color:#222;font-size:15px;font-weight:600;margin:0 0 10px;'>Your reset code</p>
    <p style='color:#555;font-size:14px;line-height:1.7;margin:0 0 24px;'>
      Use the code below to reset your password. It is valid for <strong>15 minutes</strong>.
    </p>
    <div style='background:#f4f8f4;border:2px solid #3A6B4A;border-radius:12px;padding:26px 16px;text-align:center;margin:0 0 24px;'>
      <p style='font-size:46px;font-weight:900;letter-spacing:12px;color:#3A6B4A;margin:0;font-family:monospace;'>{code}</p>
    </div>
    <p style='color:#999;font-size:13px;line-height:1.6;margin:0;'>
      If you did not request a password reset, simply ignore this email. Your password will not be changed.
    </p>
  </div>
  {EmailFooter("en")}
</div>
</body></html>";

        private string BuildResetHtmlAr(string code) => $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;direction:rtl;'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>
  {EmailHeader("إعادة تعيين كلمة المرور")}
  <div style='background:#fff;padding:28px;'>
    <p style='color:#222;font-size:15px;font-weight:600;margin:0 0 10px;'>رمز إعادة التعيين الخاص بك</p>
    <p style='color:#555;font-size:14px;line-height:1.7;margin:0 0 24px;'>
      استخدم الرمز أدناه لإعادة تعيين كلمة المرور الخاصة بك. صالح لمدة <strong>15 دقيقة</strong>.
    </p>
    <div style='background:#f4f8f4;border:2px solid #3A6B4A;border-radius:12px;padding:26px 16px;text-align:center;margin:0 0 24px;'>
      <p style='font-size:46px;font-weight:900;letter-spacing:12px;color:#3A6B4A;margin:0;font-family:monospace;direction:ltr;'>{code}</p>
    </div>
    <p style='color:#999;font-size:13px;line-height:1.6;margin:0;'>
      إذا لم تطلب إعادة تعيين كلمة المرور، فتجاهل هذا البريد الإلكتروني ببساطة. لن يتم تغيير كلمة المرور الخاصة بك.
    </p>
  </div>
  {EmailFooter("ar")}
</div>
</body></html>";
    }

    public class RegisterRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;
        [Required]
        public string Prenom { get; set; } = string.Empty;
        [Required]
        public string Nom { get; set; } = string.Empty;
        public string Language { get; set; } = "fr";
    }

    public class ChangePasswordRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;
        [Required, MinLength(6)]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ForgotPasswordRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        public string? Language { get; set; }
    }

    public class ResetPasswordRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        [Required]
        public string Code { get; set; } = string.Empty;
        [Required, MinLength(6)]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class InternalCreateRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;
        [Required]
        public string Prenom { get; set; } = string.Empty;
        [Required]
        public string Nom { get; set; } = string.Empty;
        public string Role { get; set; } = "User";
    }

    public class UpdateRoleRequest
    {
        [Required]
        public string Role { get; set; } = "User";
    }

    public class UpdateLanguageRequest
    {
        [Required]
        public string Language { get; set; } = "fr";
    }
}
