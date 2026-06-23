using Microsoft.AspNetCore.Mvc;
using MimeKit;
using QabrWebApp.Request;
using QabrWebApp.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace QabrWebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContactController : ControllerBase
    {
        private readonly IEmailService _emailService;
        private readonly IConfiguration _config;
        private readonly ILogger<ContactController> _logger;

        public ContactController(IEmailService emailService, IConfiguration config, ILogger<ContactController> logger)
        {
            _emailService = emailService;
            _config = config;
            _logger = logger;
        }

        [HttpPost]
        [SwaggerOperation(Summary = "Envoie un message de contact au support")]
        public async Task<IActionResult> Send([FromBody] ContactRequest req)
        {
            var supportEmail = _config["EmailSettings:RecipientEmail"] ?? "support@salatjanaza.org";

            _ = Task.Run(async () =>
            {
                try
                {
                    var msg = new MimeMessage();
                    msg.Subject = $"[Contact app] Message de {req.Nom}";
                    msg.Body = BuildContactEmailBody(req);
                    await _emailService.SendRawAsync(supportEmail, msg);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur envoi email contact de {Email}", req.Email);
                }
            });

            return Ok(new { message = "Votre message a bien été envoyé." });
        }

        private static MimeEntity BuildContactEmailBody(ContactRequest req)
        {
            var html = $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>

  <div style='background:#3A6B4A;padding:20px 28px;'>
    <div style='color:#fff;font-size:20px;font-weight:800;letter-spacing:0.3px;'>Salat Janaza</div>
    <div style='color:rgba(255,255,255,0.65);font-size:11px;letter-spacing:1.5px;text-transform:uppercase;margin-top:2px;'>Nouveau message de contact</div>
  </div>

  <div style='background:#fff;padding:22px 28px;border-bottom:1px solid #e8f0e9;'>
    <table style='width:100%;border-collapse:collapse;font-size:14px;'>
      <tr><td style='padding:6px 0;color:#888;width:80px;'>Nom</td><td style='padding:6px 0;color:#222;font-weight:600;'>{System.Net.WebUtility.HtmlEncode(req.Nom)}</td></tr>
      <tr><td style='padding:6px 0;color:#888;'>Email</td><td style='padding:6px 0;color:#222;'><a href='mailto:{System.Net.WebUtility.HtmlEncode(req.Email)}' style='color:#3A6B4A;'>{System.Net.WebUtility.HtmlEncode(req.Email)}</a></td></tr>
    </table>
  </div>

  <div style='background:#fff;padding:22px 28px;'>
    <p style='color:#888;font-size:12px;text-transform:uppercase;letter-spacing:1px;margin:0 0 12px;'>Message</p>
    <div style='background:#f4f8f4;border-left:4px solid #3A6B4A;border-radius:0 8px 8px 0;padding:14px 18px;'>
      <p style='color:#333;font-size:14px;line-height:1.8;margin:0;white-space:pre-wrap;'>{System.Net.WebUtility.HtmlEncode(req.Message)}</p>
    </div>
  </div>

  <div style='background:#3A6B4A;padding:18px 28px;text-align:center;'>
    <p style='color:rgba(255,255,255,0.9);font-size:13px;margin:0;'>
      Répondre à <a href='mailto:{System.Net.WebUtility.HtmlEncode(req.Email)}' style='color:#ffffff !important;font-weight:700;text-decoration:none;'>{System.Net.WebUtility.HtmlEncode(req.Email)}</a>
    </p>
  </div>

</div>
</body></html>";

            return new TextPart("html") { Text = html };
        }
    }
}
