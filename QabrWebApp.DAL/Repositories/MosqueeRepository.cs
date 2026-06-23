using Microsoft.EntityFrameworkCore;
using QabrWebApp.Dal.Entities;
using QabrWebApp.Domain.Repositories;
using DomainModel = QabrWebApp.Domain.Models;

namespace QabrWebApp.Dal.Repositories
{
    public class MosqueeRepository : IMosqueeRepository
    {
        private readonly QabrWebAppDatabaseContext _ctx;

        public MosqueeRepository(QabrWebAppDatabaseContext ctx) => _ctx = ctx;

        public async Task<List<DomainModel.Mosquee>> GetAllAsync()
        {
            var entities = await _ctx.Mosquees.AsNoTracking().Where(m => m.Statut == "Validee").ToListAsync();
            return entities.Select(ToModel).ToList();
        }

        public async Task<List<DomainModel.Mosquee>> GetPendingAsync()
        {
            var entities = await _ctx.Mosquees.AsNoTracking().Where(m => m.Statut == "EnAttente").ToListAsync();
            return entities.Select(ToModel).ToList();
        }

        public async Task<List<DomainModel.Mosquee>> GetContributionsAsync()
        {
            var entities = await _ctx.Mosquees.AsNoTracking()
                .Where(m => m.Statut == "Validee" && m.OsmId == null)
                .ToListAsync();
            return entities.Select(ToModel).ToList();
        }

        public async Task<DomainModel.Mosquee?> GetByIdAsync(int id)
        {
            var e = await _ctx.Mosquees.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
            return e is null ? null : ToModel(e);
        }

        public async Task<List<DomainModel.Mosquee>> GetNearbyAsync(double latitude, double longitude, double radiusKm)
        {
            double delta = radiusKm / 111.0;
            var candidates = await _ctx.Mosquees
                .AsNoTracking()
                .Where(m => m.Statut == "Validee"
                         && m.Latitude >= latitude - delta && m.Latitude <= latitude + delta
                         && m.Longitude >= longitude - delta && m.Longitude <= longitude + delta)
                .ToListAsync();

            return candidates
                .Where(m => HaversineKm(latitude, longitude, m.Latitude, m.Longitude) <= radiusKm)
                .Select(ToModel)
                .ToList();
        }

        public async Task<List<string>> UpsertBulkFromOsmAsync(List<DomainModel.Mosquee> mosquees)
        {
            var suppressedOsmIds = new List<string>();
            var osmIds = mosquees
                .Where(m => !string.IsNullOrEmpty(m.OsmId))
                .Select(m => m.OsmId!)
                .ToList();
            if (osmIds.Count == 0) return suppressedOsmIds;

            var existing = await _ctx.Mosquees
                .Where(m => m.OsmId != null && osmIds.Contains(m.OsmId))
                .ToDictionaryAsync(m => m.OsmId!);

            var cutoff = DateTime.UtcNow.AddDays(-30);
            // 0.002° ≈ 220m — larger tolerance covers Nominatim vs Overpass coordinate discrepancies
            const double coordTolerance = 0.002;

            // Précharge les entités existantes dans la zone (avec tracking pour pouvoir modifier l'OsmId)
            var minLat = mosquees.Min(m => m.Latitude) - coordTolerance;
            var maxLat = mosquees.Max(m => m.Latitude) + coordTolerance;
            var minLon = mosquees.Min(m => m.Longitude) - coordTolerance;
            var maxLon = mosquees.Max(m => m.Longitude) + coordTolerance;
            var nearbyEntities = await _ctx.Mosquees
                .Where(m => m.Statut != "Supprimee"
                         && m.Latitude >= minLat && m.Latitude <= maxLat
                         && m.Longitude >= minLon && m.Longitude <= maxLon)
                .ToListAsync(); // tracked — no AsNoTracking — pour pouvoir mettre à jour l'OsmId

            foreach (var m in mosquees.Where(m => !string.IsNullOrEmpty(m.OsmId)))
            {
                if (existing.TryGetValue(m.OsmId!, out var entity))
                {
                    // Mosquée supprimée par l'admin → ne pas la réactiver via sync
                    if (entity.Statut == "Supprimee") { suppressedOsmIds.Add(m.OsmId!); continue; }

                    if (entity.DerniereSyncOsm == null || entity.DerniereSyncOsm < cutoff)
                    {
                        entity.Nom = m.Nom;
                        if (!string.IsNullOrEmpty(m.Adresse)) entity.Adresse = m.Adresse;
                        entity.Latitude = m.Latitude;
                        entity.Longitude = m.Longitude;
                        entity.DerniereSyncOsm = DateTime.UtcNow;
                    }
                }
                else
                {
                    // Recherche par coordonnées pour rattacher les mosquées créées manuellement
                    var coordMatch = nearbyEntities.FirstOrDefault(e =>
                        Math.Abs(e.Latitude - m.Latitude) < coordTolerance &&
                        Math.Abs(e.Longitude - m.Longitude) < coordTolerance);

                    if (coordMatch != null)
                    {
                        // Mosquée existante sans OsmId → on lui rattache l'OsmId OSM pour éviter
                        // les doublons lors des syncs suivantes
                        if (string.IsNullOrEmpty(coordMatch.OsmId))
                        {
                            coordMatch.OsmId = m.OsmId;
                            coordMatch.Source = "osm";
                            coordMatch.DerniereSyncOsm = DateTime.UtcNow;
                            existing[m.OsmId!] = coordMatch;
                        }
                        // Si elle a déjà un OsmId différent, c'est une mosquée distincte — on ignore
                    }
                    else if (IsValidForStorage(m))
                    {
                        var newMosque = new Mosquee
                        {
                            Nom = m.Nom,
                            Adresse = m.Adresse,
                            Latitude = m.Latitude,
                            Longitude = m.Longitude,
                            OsmId = m.OsmId,
                            Source = "osm",
                            Statut = "Validee",
                            DateCreation = DateTime.UtcNow,
                            DerniereSyncOsm = DateTime.UtcNow,
                        };
                        _ctx.Mosquees.Add(newMosque);
                        nearbyEntities.Add(newMosque); // évite doublons dans le même batch
                    }
                }
            }

            await _ctx.SaveChangesAsync();
            return suppressedOsmIds;
        }

        public async Task<List<DomainModel.Mosquee>> SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return [];

            // Découpe par espaces + apostrophes (droite U+0027 et typographique U+2019)
            // "mosquée d'evry" → ["mosquee", "evry"] ; chaque mot cherché avec AND + CI+AI
            var words = query.Trim()
                .Split(new[] { ' ', '\'', '’', '-', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 2)
                .ToArray();

            if (words.Length == 0) return [];

            IQueryable<Mosquee> dbQuery = _ctx.Mosquees.AsNoTracking()
                .Where(m => m.Statut == "Validee");

            foreach (var word in words)
            {
                var w = word;
                dbQuery = dbQuery.Where(m =>
                    EF.Functions.Collate(m.Nom, "Latin1_General_CI_AI").Contains(w)
                    || (m.Adresse != null && EF.Functions.Collate(m.Adresse, "Latin1_General_CI_AI").Contains(w)));
            }

            var entities = await dbQuery.OrderBy(m => m.Nom).Take(20).ToListAsync();
            return entities.Select(ToModel).ToList();
        }

        public async Task<DomainModel.Mosquee?> GetByOsmIdAsync(string osmId)
        {
            var e = await _ctx.Mosquees.AsNoTracking().FirstOrDefaultAsync(m => m.OsmId == osmId);
            return e is null ? null : ToModel(e);
        }

        public async Task<DomainModel.Mosquee?> GetByCoordinatesAsync(double latitude, double longitude)
        {
            const double tolerance = 0.001; // ~110m, couvre les écarts entre sources (Nominatim vs Overpass)
            var e = await _ctx.Mosquees.AsNoTracking()
                .Where(m => m.Statut == "Validee"
                         && m.Latitude >= latitude - tolerance && m.Latitude <= latitude + tolerance
                         && m.Longitude >= longitude - tolerance && m.Longitude <= longitude + tolerance)
                .FirstOrDefaultAsync();
            return e is null ? null : ToModel(e);
        }

        public async Task<DomainModel.Mosquee> CreateAsync(DomainModel.Mosquee mosquee)
        {
            var entity = ToEntity(mosquee);
            _ctx.Mosquees.Add(entity);
            await _ctx.SaveChangesAsync();
            mosquee.Id = entity.Id;
            return mosquee;
        }

        public async Task<DomainModel.Mosquee> UpdateAsync(DomainModel.Mosquee mosquee)
        {
            var entity = await _ctx.Mosquees.FindAsync(mosquee.Id)
                ?? throw new KeyNotFoundException($"Mosquee {mosquee.Id} introuvable");
            entity.Nom = mosquee.Nom;
            entity.Adresse = mosquee.Adresse;
            entity.Latitude = mosquee.Latitude;
            entity.Longitude = mosquee.Longitude;
            entity.OsmId = mosquee.OsmId;
            entity.Source = mosquee.Source;
            entity.DerniereSyncOsm = mosquee.DerniereSyncOsm;
            await _ctx.SaveChangesAsync();
            return ToModel(entity);
        }

        public async Task ValiderAsync(int id)
        {
            var entity = await _ctx.Mosquees.FindAsync(id);
            if (entity is not null) { entity.Statut = "Validee"; await _ctx.SaveChangesAsync(); }
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _ctx.Mosquees.FindAsync(id);
            if (entity is not null) { entity.Statut = "Supprimee"; await _ctx.SaveChangesAsync(); }
        }

        private static bool IsValidForStorage(DomainModel.Mosquee m)
            => !string.IsNullOrWhiteSpace(m.Adresse)
            && m.Latitude is >= -90 and <= 90
            && m.Longitude is >= -180 and <= 180
            && (m.Latitude != 0 || m.Longitude != 0);

        private static DomainModel.Mosquee ToModel(Mosquee e) => new()
        {
            Id = e.Id, Nom = e.Nom, Adresse = e.Adresse,
            Latitude = e.Latitude, Longitude = e.Longitude,
            OsmId = e.OsmId, Statut = e.Statut, Source = e.Source,
            DateCreation = e.DateCreation, DerniereSyncOsm = e.DerniereSyncOsm,
        };

        private static Mosquee ToEntity(DomainModel.Mosquee m) => new()
        {
            Id = m.Id, Nom = m.Nom, Adresse = m.Adresse,
            Latitude = m.Latitude, Longitude = m.Longitude,
            OsmId = m.OsmId, Statut = m.Statut, Source = m.Source,
            DateCreation = m.DateCreation, DerniereSyncOsm = m.DerniereSyncOsm,
        };

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
    }
}
