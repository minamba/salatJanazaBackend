using QabrWebApp.Domain.Models;

namespace QabrWebApp.Domain.Repositories
{
    public interface IPriereJanazaRepository
    {
        Task<List<PriereJanaza>> GetAllAsync();
        Task<PriereJanaza?> GetByIdAsync(int id);
        Task<List<PriereJanaza>> GetByMosqueeIdAsync(int mosqueeId);
        Task<List<PriereJanaza>> GetByUtilisateurIdAsync(int utilisateurId);
        Task<List<PriereJanaza>> GetUpcomingAsync();
        Task<PriereJanaza> CreateAsync(PriereJanaza priere);
        Task<PriereJanaza> UpdateAsync(PriereJanaza priere);
        Task DeleteAsync(int id);
    }
}
