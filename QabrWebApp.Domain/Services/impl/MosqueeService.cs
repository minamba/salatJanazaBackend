using QabrWebApp.Domain.Models;
using QabrWebApp.Domain.Repositories;

namespace QabrWebApp.Domain.Services.impl
{
    public class MosqueeService : IMosqueeService
    {
        private readonly IMosqueeRepository _repo;

        public MosqueeService(IMosqueeRepository repo) => _repo = repo;

        public Task<List<Mosquee>> GetAllAsync() => _repo.GetAllAsync();
        public Task<List<Mosquee>> GetPendingAsync() => _repo.GetPendingAsync();
        public Task<List<Mosquee>> GetContributionsAsync() => _repo.GetContributionsAsync();

        public Task<Mosquee?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);

        public Task<Mosquee?> GetByOsmIdAsync(string osmId) => _repo.GetByOsmIdAsync(osmId);

        public Task<List<Mosquee>> GetNearbyAsync(double latitude, double longitude, double radiusKm)
            => _repo.GetNearbyAsync(latitude, longitude, radiusKm);

        public Task<List<Mosquee>> SearchAsync(string query)
            => _repo.SearchAsync(query);

        public async Task<Mosquee> CreateAsync(Mosquee mosquee)
        {
            var existing = await _repo.GetByCoordinatesAsync(mosquee.Latitude, mosquee.Longitude);
            if (existing != null) return existing;
            mosquee.DateCreation = DateTime.UtcNow;
            mosquee.Statut = "Validee";
            return await _repo.CreateAsync(mosquee);
        }

        public Task<Mosquee> CreateSuggestionAsync(Mosquee mosquee)
        {
            mosquee.DateCreation = DateTime.UtcNow;
            mosquee.Statut = "EnAttente";
            return _repo.CreateAsync(mosquee);
        }

        public async Task<Mosquee> UpsertFromOsmAsync(Mosquee mosquee)
        {
            if (mosquee.OsmId != null)
            {
                var existing = await _repo.GetByOsmIdAsync(mosquee.OsmId);
                if (existing != null)
                {
                    var cutoff = DateTime.UtcNow.AddDays(-30);
                    if (existing.DerniereSyncOsm == null || existing.DerniereSyncOsm < cutoff)
                    {
                        existing.Nom = mosquee.Nom;
                        if (!string.IsNullOrEmpty(mosquee.Adresse)) existing.Adresse = mosquee.Adresse;
                        existing.Latitude = mosquee.Latitude;
                        existing.Longitude = mosquee.Longitude;
                        existing.DerniereSyncOsm = DateTime.UtcNow;
                        return await _repo.UpdateAsync(existing);
                    }
                    return existing;
                }
            }
            mosquee.Source = "osm";
            mosquee.Statut = "Validee";
            mosquee.DateCreation = DateTime.UtcNow;
            mosquee.DerniereSyncOsm = DateTime.UtcNow;
            return await _repo.CreateAsync(mosquee);
        }

        public Task<List<string>> UpsertBulkFromOsmAsync(List<Mosquee> mosquees)
            => _repo.UpsertBulkFromOsmAsync(mosquees);

        public Task<Mosquee> UpdateAsync(Mosquee mosquee) => _repo.UpdateAsync(mosquee);

        public Task<NormalisationResult> NormaliserSansNomAsync() => _repo.NormaliserSansNomAsync();

        public Task ValiderAsync(int id) => _repo.ValiderAsync(id);

        public Task DeleteAsync(int id) => _repo.DeleteAsync(id);
    }
}
