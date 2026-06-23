using QabrWebApp.Domain.Models;

namespace QabrWebApp.Domain.Services
{
    public interface IUtilisateurService
    {
        Task<List<Utilisateur>> GetAllAsync();
        Task<Utilisateur?> GetByIdAsync(int id);
        Task<Utilisateur?> GetByIdentityIdAsync(string identityUserId);
        Task<Utilisateur> CreateAsync(Utilisateur utilisateur);
        Task<Utilisateur> UpdateAsync(Utilisateur utilisateur);
        Task DeleteAsync(int id);
    }
}
