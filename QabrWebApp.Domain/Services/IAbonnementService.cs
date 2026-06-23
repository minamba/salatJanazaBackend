using QabrWebApp.Domain.Models;

namespace QabrWebApp.Domain.Services
{
    public interface IAbonnementService
    {
        Task<List<Abonnement>> GetByUtilisateurIdAsync(int utilisateurId);
        Task<Abonnement?> GetByIdAsync(int id);
        Task<bool> ExistsAsync(int utilisateurId, int mosqueeId);
        Task<Abonnement> CreateAsync(Abonnement abonnement);
        Task<Abonnement> UpdateAsync(Abonnement abonnement);
        Task DeleteAsync(int id);
    }
}
