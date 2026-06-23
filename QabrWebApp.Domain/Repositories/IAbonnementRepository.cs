using QabrWebApp.Domain.Models;

namespace QabrWebApp.Domain.Repositories
{
    public interface IAbonnementRepository
    {
        Task<List<Abonnement>> GetByUtilisateurIdAsync(int utilisateurId);
        Task<List<Abonnement>> GetByMosqueeIdAsync(int mosqueeId);
        Task<Abonnement?> GetByIdAsync(int id);
        Task<bool> ExistsAsync(int utilisateurId, int mosqueeId);
        Task<Abonnement> CreateAsync(Abonnement abonnement);
        Task<Abonnement> UpdateAsync(Abonnement abonnement);
        Task DeleteAsync(int id);
    }
}
