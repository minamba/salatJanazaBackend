using Microsoft.EntityFrameworkCore;
using QabrWebApp.Dal.Entities;
using QabrWebApp.Domain.Repositories;
using DomainModel = QabrWebApp.Domain.Models;

namespace QabrWebApp.Dal.Repositories
{
    public class RappelPushRepository : IRappelPushRepository
    {
        private readonly QabrWebAppDatabaseContext _ctx;

        public RappelPushRepository(QabrWebAppDatabaseContext ctx) => _ctx = ctx;

        public async Task CreateAsync(DomainModel.RappelPush rappel)
        {
            _ctx.RappelsPush.Add(new RappelPush
            {
                MosqueeId = rappel.MosqueeId,
                PriereJanazaId = rappel.PriereJanazaId,
                DateEnvoi = rappel.DateEnvoi,
                CreatedAt = rappel.CreatedAt,
            });
            await _ctx.SaveChangesAsync();
        }

        public async Task<List<DomainModel.RappelPush>> GetPendingAsync()
        {
            var entities = await _ctx.RappelsPush
                .AsNoTracking()
                .Where(r => r.DateEnvoi <= DateTime.UtcNow && r.EnvoyeAt == null)
                .ToListAsync();

            return entities.Select(e => new DomainModel.RappelPush
            {
                Id = e.Id,
                MosqueeId = e.MosqueeId,
                PriereJanazaId = e.PriereJanazaId,
                DateEnvoi = e.DateEnvoi,
                CreatedAt = e.CreatedAt,
            }).ToList();
        }

        public async Task MarkSentAsync(int id)
        {
            var entity = await _ctx.RappelsPush.FindAsync(id);
            if (entity is null) return;
            entity.EnvoyeAt = DateTime.UtcNow;
            await _ctx.SaveChangesAsync();
        }
    }
}
