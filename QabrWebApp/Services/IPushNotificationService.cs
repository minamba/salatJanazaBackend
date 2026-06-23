using QabrWebApp.Domain.Models;

namespace QabrWebApp.Services
{
    public interface IPushNotificationService
    {
        Task NotifyMosqueeSubscribersAsync(int mosqueeId, PriereJanaza priere);
        Task ScheduleMosqueeReminderAsync(int mosqueeId, PriereJanaza priere);
        Task SendToTokenAsync(string expoToken, string title, string body, object? data = null);
        Task SendToTokensAsync(IEnumerable<string> tokens, string title, string body, object? data = null);
    }
}
