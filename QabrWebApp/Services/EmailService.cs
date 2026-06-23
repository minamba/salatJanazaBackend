using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace QabrWebApp.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendNotificationAsync(string recipientEmail, string subject, string htmlBody)
        {
            var settings = _config.GetSection("EmailSettings");
            var host = settings["SmtpHost"]!;
            var port = int.Parse(settings["SmtpPort"]!);
            var senderEmail = settings["SenderEmail"]!;
            var password = settings["SenderPassword"]!;

            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(senderEmail));
            message.To.Add(MailboxAddress.Parse(recipientEmail));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(senderEmail, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        public async Task SendRawAsync(string recipientEmail, MimeMessage message)
        {
            var settings = _config.GetSection("EmailSettings");
            message.From.Add(MailboxAddress.Parse(settings["SenderEmail"]!));
            message.To.Add(MailboxAddress.Parse(recipientEmail));

            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync(settings["SmtpHost"]!, int.Parse(settings["SmtpPort"]!), SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(settings["SenderEmail"]!, settings["SenderPassword"]!);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}
