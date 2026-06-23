using QabrWebApp.Domain.Models;
using QabrWebApp.Domain.Repositories;

namespace QabrWebApp.Domain.Services.impl
{
    public class AbonnementService : IAbonnementService
    {
        private readonly IAbonnementRepository _repo;

        public AbonnementService(IAbonnementRepository repo) => _repo = repo;

        public Task<List<Abonnement>> GetByUtilisateurIdAsync(int utilisateurId) => _repo.GetByUtilisateurIdAsync(utilisateurId);

        public Task<Abonnement?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);

        public Task<bool> ExistsAsync(int utilisateurId, int mosqueeId) => _repo.ExistsAsync(utilisateurId, mosqueeId);

        public Task<Abonnement> CreateAsync(Abonnement abonnement)
        {
            abonnement.DateAbonnement = DateTime.UtcNow;
            abonnement.NotifActive = true;
            return _repo.CreateAsync(abonnement);
        }

        public Task<Abonnement> UpdateAsync(Abonnement abonnement) => _repo.UpdateAsync(abonnement);

        public Task DeleteAsync(int id) => _repo.DeleteAsync(id);
    }
}
