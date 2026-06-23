using MimeKit;

namespace QabrWebApp.Services
{
    public interface IEmailService
    {
        Task SendNotificationAsync(string recipientEmail, string subject, string htmlBody);
        Task SendRawAsync(string recipientEmail, MimeMessage message);
    }
}
