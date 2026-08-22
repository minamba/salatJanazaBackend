using QabrWebApp.Domain.Models;

namespace QabrWebApp.Services
{
    public interface IPushNotificationService
    {
        Task NotifyMosqueeSubscribersAsync(int mosqueeId, PriereJanaza priere);
        Task ScheduleMosqueeReminderAsync(int mosqueeId, PriereJanaza priere);
        Task RescheduleMosqueeReminderAsync(int mosqueeId, PriereJanaza priere);
        Task SendToTokenAsync(string expoToken, string title, string body, object? data = null);
        Task SendToTokensAsync(IEnumerable<string> tokens, string title, string body, object? data = null);
        Task SendPermissionUpdateAsync(string expoToken, bool canImportFlyer);
        Task SendPermissionUpdateToManyAsync(IEnumerable<string> expoTokens, bool canImportFlyer);
        Task NotifyRadiusUsersAsync(int mosqueeId, PriereJanaza priere);

        /// <summary>
        /// Tous ceux qui doivent entendre parler de cette mosquée : ses abonnés,
        /// plus les utilisateurs qui l'ont dans leur rayon sans y être abonnés.
        /// Dédoublonné par jeton — personne ne reçoit deux fois.
        /// </summary>
        Task<IReadOnlyList<(string Token, string Language)>> GetDestinatairesMosqueeAsync(int mosqueeId);

        /// <summary>
        /// Envoie une notification à tous les utilisateurs de l'application (tokens modernes + hérités).
        /// </summary>
        Task SendToAllUsersAsync(string title, string body);
    }
}
