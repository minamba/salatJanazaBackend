using QabrWebApp.Domain.Models;
using QabrWebApp.Domain.Repositories;

namespace QabrWebApp.Domain.Services.impl
{
    public class PriereJanazaService : IPriereJanazaService
    {
        private readonly IPriereJanazaRepository _repo;

        public PriereJanazaService(IPriereJanazaRepository repo) => _repo = repo;

        public Task<List<PriereJanaza>> GetAllAsync() => _repo.GetAllAsync();

        public Task<PriereJanaza?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);

        public Task<List<PriereJanaza>> GetByMosqueeIdAsync(int mosqueeId) => _repo.GetByMosqueeIdAsync(mosqueeId);

        public Task<List<PriereJanaza>> GetByUtilisateurIdAsync(int utilisateurId) => _repo.GetByUtilisateurIdAsync(utilisateurId);

        public Task<List<PriereJanaza>> GetUpcomingAsync() => _repo.GetUpcomingAsync();

        public Task<List<PriereJanaza>> GetPendingAsync() => _repo.GetPendingAsync();

        public Task ActivatePendingByMosqueeAsync(int mosqueeId) => _repo.ActivatePendingByMosqueeAsync(mosqueeId);

        public Task<PriereJanaza> CreateAsync(PriereJanaza priere)
        {
            priere.DateCreation = DateTime.UtcNow;
            if (priere.Statut != StatutPriere.EnAttente)
                // Comparer le vrai UTC de la prière (wall-clock - offset) avec l'heure actuelle.
                priere.Statut = priere.DateHeurePriere.AddMinutes(-priere.UtcOffsetMinutes) > DateTime.UtcNow ? StatutPriere.AVenir : StatutPriere.EnCours;
            return _repo.CreateAsync(priere);
        }

        public Task<PriereJanaza> UpdateAsync(PriereJanaza priere) => _repo.UpdateAsync(priere);

        public Task DeleteAsync(int id) => _repo.DeleteAsync(id);
    }
}
