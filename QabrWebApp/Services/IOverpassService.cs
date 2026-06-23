using QabrWebApp.Domain.Models;

namespace QabrWebApp.Services
{
    public interface IOverpassService
    {
        Task<List<Mosquee>> FetchMosqueesAsync(double latitude, double longitude, double radiusKm);
    }
}
