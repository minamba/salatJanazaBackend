using QabrWebApp.Domain.Models;
using QabrWebApp.Domain.Repositories;

namespace QabrWebApp.Domain.Services.impl
{
    public class UtilisateurService : IUtilisateurService
    {
        private readonly IUtilisateurRepository _repo;

        public UtilisateurService(IUtilisateurRepository repo) => _repo = repo;

        public Task<List<Utilisateur>> GetAllAsync() => _repo.GetAllAsync();

        public Task<Utilisateur?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);

        public Task<Utilisateur?> GetByIdentityIdAsync(string identityUserId) => _repo.GetByIdentityIdAsync(identityUserId);

        public Task<Utilisateur> CreateAsync(Utilisateur utilisateur)
        {
            utilisateur.DateInscription = DateTime.UtcNow;
            return _repo.CreateAsync(utilisateur);
        }

        public Task<Utilisateur> UpdateAsync(Utilisateur utilisateur) => _repo.UpdateAsync(utilisateur);

        public Task DeleteAsync(int id) => _repo.DeleteAsync(id);

        public Task<int> BulkSetCanImportFlyerAsync(bool canImportFlyer) => _repo.BulkSetCanImportFlyerAsync(canImportFlyer);

        public Task<List<string>> GetExpoTokensPageAsync(string role, int offset, int limit) => _repo.GetExpoTokensPageAsync(role, offset, limit);
    }
}
