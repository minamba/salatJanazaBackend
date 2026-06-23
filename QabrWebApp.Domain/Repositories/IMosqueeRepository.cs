using QabrWebApp.Domain.Models;

namespace QabrWebApp.Domain.Repositories
{
    public interface IMosqueeRepository
    {
        Task<List<Mosquee>> GetAllAsync();
        Task<List<Mosquee>> GetPendingAsync();
        Task<List<Mosquee>> GetContributionsAsync();
        Task<Mosquee?> GetByIdAsync(int id);
        Task<List<Mosquee>> GetNearbyAsync(double latitude, double longitude, double radiusKm);
        Task<List<Mosquee>> SearchAsync(string query);
        Task<Mosquee?> GetByOsmIdAsync(string osmId);
        Task<Mosquee?> GetByCoordinatesAsync(double latitude, double longitude);
        Task<Mosquee> CreateAsync(Mosquee mosquee);
        Task<Mosquee> UpdateAsync(Mosquee mosquee);
        Task<List<string>> UpsertBulkFromOsmAsync(List<Mosquee> mosquees);
        Task ValiderAsync(int id);
        Task DeleteAsync(int id);
    }
}
