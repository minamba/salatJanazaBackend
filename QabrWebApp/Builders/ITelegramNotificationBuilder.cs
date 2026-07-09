using QabrWebApp.Domain.Models;

namespace QabrWebApp.Builders
{
    public interface ITelegramNotificationBuilder
    {
        Task NotifyNewJanazaAsync(PriereJanaza priere, string mosqueeNom, string? mosqueeAdresse, Utilisateur? utilisateur);
        Task NotifyPendingJanazaAsync(PriereJanaza priere, string mosqueeNom);
        Task NotifyPendingMosqueeAsync(Mosquee mosquee, string? utilisateurEmail);
    }
}
