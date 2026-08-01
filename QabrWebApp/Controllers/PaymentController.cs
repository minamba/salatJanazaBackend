using Microsoft.AspNetCore.Mvc;
using MimeKit;
using QabrWebApp.Services;
using Stripe;
using Stripe.Checkout;

namespace QabrWebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IEmailService _email;
        private readonly IConfiguration _config;

        public PaymentController(IEmailService email, IConfiguration config)
        {
            _email = email;
            _config = config;
        }

        [HttpPost("checkout")]
        public async Task<IActionResult> CreateCheckout([FromBody] CreateCheckoutRequest req)
        {
            if (req.AmountCents < 50)
                return BadRequest(new { error = "Montant minimum 0,50 €" });

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "eur",
                            UnitAmount = req.AmountCents,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = "Don — Salat Janaza",
                            },
                        },
                        Quantity = 1,
                    },
                },
                Mode = "payment",
                SuccessUrl = "https://salatjanaza.org/payment-success",
                CancelUrl  = "https://salatjanaza.org/soutenez-nous",
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);
            return Ok(new { url = session.Url });
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            Request.EnableBuffering();
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var signature = Request.Headers["Stripe-Signature"].ToString();
            var secret = _config["Stripe:WebhookSecret"] ?? "";

            try
            {
                var stripeEvent = EventUtility.ConstructEvent(json, signature, secret);

                if (stripeEvent.Type == EventTypes.CheckoutSessionCompleted)
                {
                    var session = stripeEvent.Data.Object as Session;
                    if (session?.CustomerEmail is { } customerEmail && session.PaymentStatus == "paid")
                    {
                        var montant = (session.AmountTotal ?? 0) / 100m;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var html = BuildDonationThanksHtml(montant);
                                var message = BuildMimeMessage(html);
                                await _email.SendRawAsync(customerEmail, message);
                            }
                            catch { }
                        });
                    }
                }

                return Ok();
            }
            catch (StripeException)
            {
                return BadRequest();
            }
        }

        private static string BuildDonationThanksHtml(decimal montant) => $@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'></head>
<body style='margin:0;padding:0;background:#f4f8f4;font-family:Georgia,serif;'>
<div style='max-width:560px;margin:32px auto;border-radius:12px;overflow:hidden;box-shadow:0 2px 16px rgba(0,0,0,0.10);'>
  {EmailHeader("Remerciement · شكر وعرفان")}
  <div style='background:#fff;padding:22px 28px;border-bottom:1px solid #e8f0e9;'>
    <p style='color:#222;font-size:15px;margin:0;'>As salamou 3alaykoum wa rahmatulLahi wa barakatuh,</p>
  </div>
  <div style='background:#fff;padding:22px 28px;'>
    <div style='border-left:4px solid #3A6B4A;background:#f4f8f4;border-radius:0 8px 8px 0;padding:16px 18px;margin-bottom:20px;text-align:center;'>
      <p style='color:#3A6B4A;font-size:22px;font-weight:700;margin:0 0 6px;font-family:Georgia,serif;'>
        جَزَاكَ اللَّهُ خَيْرًا
      </p>
      <p style='color:#555;font-size:13px;font-style:italic;margin:0;'>
        Qu'Allah vous récompense pour votre don !
      </p>
    </div>
    <p style='color:#444;font-size:15px;line-height:1.8;margin:0 0 16px;'>
      Votre soutien de <strong style='color:#3A6B4A;'>{montant:0.00} €</strong> a bien été reçu.
      Il contribue directement à la maintenance et au développement de l'application Salat Janaza,
      qui aide des milliers de musulmans à s'informer et participer aux prières funéraires.
    </p>
    <p style='color:#555;font-size:14px;line-height:1.8;margin:0;'>
      Que votre acte de générosité soit une source de bienfaits pour vous dans ce monde et dans l'au-delà.
      Aameen.
    </p>
  </div>
  {EmailFooter()}
</div>
</body></html>";

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

        private static string EmailFooter() => @"
  <div style='background:#3A6B4A;padding:18px 28px;text-align:center;'>
    <p style='color:rgba(255,255,255,0.75);font-size:13px;margin:0;'>
      Une question ? <a href='mailto:support@salatjanaza.org' style='color:#fff;font-weight:600;'>support@salatjanaza.org</a>
    </p>
  </div>";

        private static MimeMessage BuildMimeMessage(string html)
        {
            var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "acc1.png");
            var htmlPart = new TextPart("html") { Text = html };
            var image = new MimePart("image", "png")
            {
                Content = new MimeContent(System.IO.File.OpenRead(logoPath)),
                ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
                ContentTransferEncoding = ContentEncoding.Base64,
                ContentId = "logo",
            };
            var message = new MimeMessage();
            message.Subject = "Merci pour votre soutien — Salat Janaza 🤲";
            message.Body = new MultipartRelated { htmlPart, image };
            return message;
        }
    }

    public record CreateCheckoutRequest(long AmountCents);
}
