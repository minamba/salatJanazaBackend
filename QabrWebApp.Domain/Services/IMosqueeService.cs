using QabrWebApp.Domain.Models;

namespace QabrWebApp.Domain.Services
{
    public interface IMosqueeService
    {
        Task<NormalisationResult> NormaliserSansNomAsync();
        Task<List<Mosquee>> GetAllAsync();
        Task<List<Mosquee>> GetPendingAsync();
        Task<List<Mosquee>> GetContributionsAsync();
        Task<Mosquee?> GetByIdAsync(int id);
        Task<Mosquee?> GetByOsmIdAsync(string osmId);
        Task<List<Mosquee>> GetNearbyAsync(double latitude, double longitude, double radiusKm);
        Task<List<Mosquee>> SearchAsync(string query);
        Task<Mosquee> CreateAsync(Mosquee mosquee);
        Task<Mosquee> CreateSuggestionAsync(Mosquee mosquee);
        Task<Mosquee> UpsertFromOsmAsync(Mosquee mosquee);
        Task<List<string>> UpsertBulkFromOsmAsync(List<Mosquee> mosquees);
        Task<Mosquee> UpdateAsync(Mosquee mosquee);
        Task ValiderAsync(int id);
        Task DeleteAsync(int id);
    }
}
