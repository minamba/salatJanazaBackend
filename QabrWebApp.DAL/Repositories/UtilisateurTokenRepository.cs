using Microsoft.EntityFrameworkCore;
using QabrWebApp.Dal.Entities;
using QabrWebApp.Domain.Repositories;

namespace QabrWebApp.Dal.Repositories
{
    public class UtilisateurTokenRepository : IUtilisateurTokenRepository
    {
        private readonly QabrWebAppDatabaseContext _ctx;

        public UtilisateurTokenRepository(QabrWebAppDatabaseContext ctx) => _ctx = ctx;

        public async Task UpsertAsync(int utilisateurId, string expoToken)
        {
            var existing = await _ctx.UtilisateurTokens
                .FirstOrDefaultAsync(t => t.UtilisateurId == utilisateurId && t.ExpoToken == expoToken);

            if (existing is null)
                _ctx.UtilisateurTokens.Add(new UtilisateurToken
                {
                    UtilisateurId = utilisateurId,
                    ExpoToken = expoToken,
                    UpdatedAt = DateTime.UtcNow,
                });
            else
                existing.UpdatedAt = DateTime.UtcNow;

            await _ctx.SaveChangesAsync();
        }

        public async Task<List<string>> GetTokensByUserIdsAsync(IEnumerable<int> utilisateurIds)
        {
            return await _ctx.UtilisateurTokens
                .Where(t => utilisateurIds.Contains(t.UtilisateurId))
                .Select(t => t.ExpoToken)
                .Distinct()
                .ToListAsync();
        }

        public async Task<List<(string Token, string Language)>> GetTokensWithLanguageByUserIdsAsync(IEnumerable<int> utilisateurIds)
        {
            var rows = await _ctx.UtilisateurTokens
                .Include(t => t.Utilisateur)
                .Where(t => utilisateurIds.Contains(t.UtilisateurId))
                .ToListAsync();
            return rows
                .DistinctBy(r => r.ExpoToken)
                .Select(r => (r.ExpoToken, r.Utilisateur?.Language ?? "fr"))
                .ToList();
        }

        public async Task<List<string>> GetAllTokensPagedAsync(int skip, int pageSize)
        {
            // UNION en SQL : tokens modernes + tokens hérités non dupliqués, dédoublonnés, paginés
            var modern = _ctx.UtilisateurTokens.Select(t => t.ExpoToken);
            var legacy = _ctx.Utilisateurs
                .Where(u => u.ExpoToken != null)
                .Select(u => u.ExpoToken!)
                .Where(t => !modern.Contains(t));

            return await modern.Union(legacy)
                .OrderBy(t => t)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync();
        }
    }
}
