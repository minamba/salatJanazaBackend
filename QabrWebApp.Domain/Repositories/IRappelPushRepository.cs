using QabrWebApp.Domain.Models;

namespace QabrWebApp.Domain.Repositories
{
    public interface IRappelPushRepository
    {
        Task CreateAsync(RappelPush rappel);
        Task<List<RappelPush>> GetPendingAsync();
        Task MarkSentAsync(int id);
        Task DeletePendingByPriereIdAsync(int priereJanazaId);
    }
}
