using Microsoft.EntityFrameworkCore;
using QabrWebApp.Dal.Entities;
using QabrWebApp.Domain.Repositories;
using DomainModel = QabrWebApp.Domain.Models;

namespace QabrWebApp.Dal.Repositories
{
    public class UtilisateurRepository : IUtilisateurRepository
    {
        private readonly QabrWebAppDatabaseContext _ctx;

        public UtilisateurRepository(QabrWebAppDatabaseContext ctx) => _ctx = ctx;

        public async Task<List<DomainModel.Utilisateur>> GetAllAsync()
        {
            var entities = await _ctx.Utilisateurs.AsNoTracking().ToListAsync();
            return entities.Select(ToModel).ToList();
        }

        public async Task<DomainModel.Utilisateur?> GetByIdAsync(int id)
        {
            var e = await _ctx.Utilisateurs.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            return e is null ? null : ToModel(e);
        }

        public async Task<DomainModel.Utilisateur?> GetByIdentityIdAsync(string identityUserId)
        {
            var e = await _ctx.Utilisateurs.AsNoTracking().FirstOrDefaultAsync(u => u.IdentityUserId == identityUserId);
            return e is null ? null : ToModel(e);
        }

        public async Task<DomainModel.Utilisateur> CreateAsync(DomainModel.Utilisateur utilisateur)
        {
            var entity = ToEntity(utilisateur);
            _ctx.Utilisateurs.Add(entity);
            await _ctx.SaveChangesAsync();
            utilisateur.Id = entity.Id;
            return utilisateur;
        }

        public async Task<DomainModel.Utilisateur> UpdateAsync(DomainModel.Utilisateur utilisateur)
        {
            var entity = await _ctx.Utilisateurs.FindAsync(utilisateur.Id)
                ?? throw new KeyNotFoundException($"Utilisateur {utilisateur.Id} introuvable");
            entity.Prenom = utilisateur.Prenom;
            entity.Nom = utilisateur.Nom;
            entity.Email = utilisateur.Email;
            entity.Telephone = utilisateur.Telephone;
            entity.ExpoToken = utilisateur.ExpoToken;
            entity.AdresseDomicile = utilisateur.AdresseDomicile;
            entity.LatitudeDomicile = utilisateur.LatitudeDomicile;
            entity.LongitudeDomicile = utilisateur.LongitudeDomicile;
            entity.RayonNotification = utilisateur.RayonNotification;
            entity.NotifMouvement = utilisateur.NotifMouvement;
            entity.Role = utilisateur.Role;
            entity.Language = utilisateur.Language;
            entity.CanImportFlyer = utilisateur.CanImportFlyer;
            entity.LatitudeCourante = utilisateur.LatitudeCourante;
            entity.LongitudeCourante = utilisateur.LongitudeCourante;
            entity.ModeLocalisation = utilisateur.ModeLocalisation;
            entity.Platform = utilisateur.Platform;
            await _ctx.SaveChangesAsync();
            return ToModel(entity);
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _ctx.Utilisateurs.FindAsync(id);
            if (entity is not null) { _ctx.Utilisateurs.Remove(entity); await _ctx.SaveChangesAsync(); }
        }

        public async Task<int> BulkSetCanImportFlyerAsync(bool canImportFlyer)
        {
            return await _ctx.Utilisateurs
                .Where(u => u.Role == "User")
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.CanImportFlyer, canImportFlyer));
        }

        public async Task<List<(int UserId, string? LegacyToken, string Language)>> GetUsersInRadiusAsync(
            double mosquéeLat, double mosquéeLon, ISet<int> excludeUserIds)
        {
            // Charge uniquement les utilisateurs qui ont une position (domicile ou courante)
            var users = await _ctx.Utilisateurs
                .AsNoTracking()
                .Where(u =>
                    (u.LatitudeDomicile != null && u.LongitudeDomicile != null) ||
                    (u.LatitudeCourante != null && u.LongitudeCourante != null))
                .Select(u => new
                {
                    u.Id,
                    u.ExpoToken,
                    u.Language,
                    u.ModeLocalisation,
                    u.LatitudeDomicile, u.LongitudeDomicile,
                    u.LatitudeCourante, u.LongitudeCourante,
                    u.RayonNotification,
                })
                .ToListAsync();

            var result = new List<(int, string?, string)>();
            foreach (var u in users)
            {
                if (excludeUserIds.Contains(u.Id)) continue;

                // Choix du centre :
                // - mode "home" explicite → adresse domicile
                // - sinon → GPS courant ; si GPS null (permission refusée ou pas encore syncé), fallback sur domicile
                double? centerLat, centerLon;
                if (u.ModeLocalisation == "home" && u.LatitudeDomicile.HasValue)
                {
                    centerLat = u.LatitudeDomicile;
                    centerLon = u.LongitudeDomicile;
                }
                else
                {
                    centerLat = u.LatitudeCourante ?? u.LatitudeDomicile;
                    centerLon = u.LongitudeCourante ?? u.LongitudeDomicile;
                }

                if (centerLat is null || centerLon is null) continue;

                var distKm = HaversineKm(centerLat.Value, centerLon.Value, mosquéeLat, mosquéeLon);
                if (distKm <= u.RayonNotification)
                    result.Add((u.Id, u.ExpoToken, u.Language ?? "fr"));
            }
            return result;
        }

        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        public Task<List<string>> GetExpoTokensPageAsync(string role, int offset, int limit)
        {
            return _ctx.Utilisateurs
                .AsNoTracking()
                .Where(u => u.Role == role && u.ExpoToken != null && u.ExpoToken != "")
                .OrderBy(u => u.Id)
                .Skip(offset)
                .Take(limit)
                .Select(u => u.ExpoToken!)
                .ToListAsync();
        }

        private static DomainModel.Utilisateur ToModel(Utilisateur e) => new()
        {
            Id = e.Id, IdentityUserId = e.IdentityUserId,
            Prenom = e.Prenom, Nom = e.Nom, Email = e.Email,
            Telephone = e.Telephone, ExpoToken = e.ExpoToken,
            AdresseDomicile = e.AdresseDomicile,
            LatitudeDomicile = e.LatitudeDomicile, LongitudeDomicile = e.LongitudeDomicile,
            RayonNotification = e.RayonNotification, NotifMouvement = e.NotifMouvement,
            DateInscription = e.DateInscription, Role = e.Role, Language = e.Language,
            CanImportFlyer = e.CanImportFlyer,
            LatitudeCourante = e.LatitudeCourante, LongitudeCourante = e.LongitudeCourante,
            ModeLocalisation = e.ModeLocalisation,
            Platform = e.Platform,
        };

        private static Utilisateur ToEntity(DomainModel.Utilisateur u) => new()
        {
            Id = u.Id, IdentityUserId = u.IdentityUserId,
            Prenom = u.Prenom, Nom = u.Nom, Email = u.Email,
            Telephone = u.Telephone, ExpoToken = u.ExpoToken,
            AdresseDomicile = u.AdresseDomicile,
            LatitudeDomicile = u.LatitudeDomicile, LongitudeDomicile = u.LongitudeDomicile,
            RayonNotification = u.RayonNotification, NotifMouvement = u.NotifMouvement,
            DateInscription = u.DateInscription, Role = u.Role, Language = u.Language,
            LatitudeCourante = u.LatitudeCourante, LongitudeCourante = u.LongitudeCourante,
            ModeLocalisation = u.ModeLocalisation,
        };
    }
}
