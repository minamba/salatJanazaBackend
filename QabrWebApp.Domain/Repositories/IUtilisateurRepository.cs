using QabrWebApp.Domain.Models;

namespace QabrWebApp.Domain.Repositories
{
    public interface IUtilisateurRepository
    {
        Task<List<Utilisateur>> GetAllAsync();
        Task<Utilisateur?> GetByIdAsync(int id);
        Task<Utilisateur?> GetByIdentityIdAsync(string identityUserId);
        Task<Utilisateur> CreateAsync(Utilisateur utilisateur);
        Task<Utilisateur> UpdateAsync(Utilisateur utilisateur);
        Task DeleteAsync(int id);
        Task<int> BulkSetCanImportFlyerAsync(bool canImportFlyer);
        Task<List<string>> GetExpoTokensPageAsync(string role, int offset, int limit);
        Task<List<(int UserId, string? LegacyToken, string Language)>> GetUsersInRadiusAsync(double lat, double lon, ISet<int> excludeUserIds);
    }
}
