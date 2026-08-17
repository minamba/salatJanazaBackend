using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QabrWebApp.Dal.Entities;
using QabrWebApp.Domain.Repositories;
using DomainModel = QabrWebApp.Domain.Models;

namespace QabrWebApp.Dal.Repositories
{
    public class MosqueeRepository : IMosqueeRepository
    {
        private readonly QabrWebAppDatabaseContext _ctx;
        private static readonly System.Net.Http.HttpClient _geoHttp = new();
        private readonly Dictionary<string, string?> _cpCache = new();

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
            double latDelta = radiusKm / 111.0;
            double lonDelta = radiusKm / (111.0 * Math.Cos(latitude * Math.PI / 180.0));
            var candidates = await _ctx.Mosquees
                .AsNoTracking()
                .Where(m => m.Statut == "Validee"
                         && m.Latitude >= latitude - latDelta && m.Latitude <= latitude + latDelta
                         && m.Longitude >= longitude - lonDelta && m.Longitude <= longitude + lonDelta)
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
                        // Ne pas écraser un nom déjà normalisé par l'admin — seulement les noms génériques
                        if (IsGenericName(entity.Nom))
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
                    else
                    {
                        // Coordonnées exactes déjà en base → skip
                        var exactMatch = nearbyEntities.Any(e =>
                            e.Latitude == m.Latitude && e.Longitude == m.Longitude);

                        // Dans un rayon de 20m d'une mosquée existante → skip
                        var tooClose = !exactMatch && nearbyEntities.Any(e =>
                            HaversineKm(m.Latitude, m.Longitude, e.Latitude, e.Longitude) <= 0.020);

                        if (exactMatch || tooClose) continue; // déjà en base — le client dédoublonne par proximité

                        // Validation : nom requis + adresse avec rue et code postal + coordonnées valides
                        if (!IsValidForStorage(m))
                        {
                            suppressedOsmIds.Add(m.OsmId!);
                            continue;
                        }

                        var nom = m.Nom!;
                        var adresse = m.Adresse;

                        // Nom générique → renommer à partir de la ville (dans l'adresse ou via CP)
                        if (IsGenericName(nom))
                        {
                            var ville = ExtractVille(adresse);
                            if (ville == null)
                            {
                                var cp = Regex.Match(adresse ?? "", @"\d{5}").Value;
                                ville = string.IsNullOrEmpty(cp) ? null : await LookupVilleParCPAsync(cp);
                                if (ville != null)
                                    adresse = $"{adresse?.TrimEnd().TrimEnd(',')}, {ville}";
                            }
                            if (ville != null)
                                nom = $"Mosquée {PrepDe(ville)}{ville}";
                            else
                            {
                                suppressedOsmIds.Add(m.OsmId!); // ville introuvable → non stockée
                                continue;
                            }
                        }

                        // Même nom + même adresse déjà en base → skip même si coordonnées différentes
                        var nomNorm = nom.Trim().ToLowerInvariant();
                        var adresseNorm = (adresse ?? "").Trim().ToLowerInvariant();
                        var dupeParAdresse =
                            nearbyEntities.Any(e =>
                                e.Nom.Trim().ToLowerInvariant() == nomNorm &&
                                (e.Adresse ?? "").Trim().ToLowerInvariant() == adresseNorm)
                            || await _ctx.Mosquees.AnyAsync(e =>
                                e.Statut != "Supprimee" &&
                                e.Nom == nom &&
                                e.Adresse == adresse);
                        if (dupeParAdresse) { suppressedOsmIds.Add(m.OsmId!); continue; }

                        var newMosque = new Mosquee
                        {
                            Nom = nom,
                            Adresse = adresse,
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
            const double tolerance = 0.0001; // ~11m, couvre les écarts entre sources (Nominatim vs Overpass) sans matcher des bâtiments différents
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
            if (entity is not null) { _ctx.Mosquees.Remove(entity); await _ctx.SaveChangesAsync(); }
        }

        private static bool IsValidForStorage(DomainModel.Mosquee m)
            => !string.IsNullOrWhiteSpace(m.Nom)
            && HasValidStreetAndPostal(m.Adresse)
            && m.Latitude is >= -90 and <= 90
            && m.Longitude is >= -180 and <= 180
            && (m.Latitude != 0 || m.Longitude != 0);

        private static readonly HashSet<string> _genericNames =
            new(StringComparer.Ordinal) { "Mosquée", "mosquée", "mosquee", "Msoquee" };

        private static bool IsGenericName(string? nom) =>
            nom != null && _genericNames.Contains(nom.Trim());

        // Résout le nom de la commune depuis un code postal via geo.api.gouv.fr
        // Le résultat est mis en cache pour éviter des appels répétés dans le même batch
        private async Task<string?> LookupVilleParCPAsync(string codePostal)
        {
            if (_cpCache.TryGetValue(codePostal, out var cached)) return cached;
            try
            {
                var json = await _geoHttp.GetStringAsync(
                    $"https://geo.api.gouv.fr/communes?codePostal={codePostal}&fields=nom&limit=1");
                var doc = JsonDocument.Parse(json);
                var ville = doc.RootElement.EnumerateArray().FirstOrDefault().TryGetProperty("nom", out var p)
                    ? TitreCasser(p.GetString() ?? "")
                    : null;
                _cpCache[codePostal] = ville;
                return ville;
            }
            catch { _cpCache[codePostal] = null; return null; }
        }

        // Adresse valide = code postal français (5 chiffres) + quelque chose avant (rue)
        private static bool HasValidStreetAndPostal(string? adresse)
        {
            if (string.IsNullOrWhiteSpace(adresse)) return false;
            var cp = Regex.Match(adresse, @"\d{5}");
            if (!cp.Success) return false;
            var avantCP = adresse[..cp.Index].Trim().Trim(',', ' ');
            return avantCP.Length >= 3;
        }

        // Extrait la ville d'une adresse française : "5 Rue X, 91000 Évry" → "Évry"
        private static string? ExtractVille(string? adresse)
        {
            if (string.IsNullOrWhiteSpace(adresse)) return null;
            var m = Regex.Match(adresse, @"\d{5}\s*,?\s*([^,\n]+)");
            if (!m.Success) return null;
            var ville = Regex.Replace(m.Groups[1].Value, @",.*$", "").Trim().TrimEnd('.', ' ');
            return string.IsNullOrWhiteSpace(ville) ? null : TitreCasser(ville);
        }

        // "Saint-Michel-Sur-Orge" → "Saint-Michel-sur-Orge" (capitalise chaque segment)
        private static string TitreCasser(string s) =>
            string.Join(" ", s.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => string.Join("-", word.Split('-')
                    .Select(p => p.Length == 0 ? p : char.ToUpper(p[0]) + (p.Length > 1 ? p[1..].ToLower() : "")))));

        // "Évry" → "d'" ; "Saint-Michel" → "de "
        private static string PrepDe(string ville)
        {
            if (string.IsNullOrEmpty(ville)) return "de ";
            const string voyelles = "AEIOUYaeiouÀÂÄÉÈÊËÎÏÔÖÙÛÜœæàâäéèêëîïôöùûü";
            return voyelles.Contains(ville[0]) ? "d'" : "de ";
        }

        public async Task<DomainModel.NormalisationResult> NormaliserSansNomAsync()
        {
            var result = new DomainModel.NormalisationResult();

            var mosquees = await _ctx.Mosquees
                .Where(m => m.Statut != "Supprimee")
                .ToListAsync();

            foreach (var mosquee in mosquees)
            {
                if (IsGenericName(mosquee.Nom))
                {
                    var ville = ExtractVille(mosquee.Adresse);
                    if (ville != null)
                    {
                        var nouveauNom = $"Mosquée {PrepDe(ville)}{ville}";
                        result.Renommes.Add(new(mosquee.Id, mosquee.Nom, nouveauNom, mosquee.Adresse));
                        mosquee.Nom = nouveauNom;
                        continue;
                    }
                    // Pas de ville extractible → traité comme adresse invalide ci-dessous
                }
                else if (HasValidStreetAndPostal(mosquee.Adresse))
                {
                    continue; // nom + adresse valide → rien à faire
                }

                // Ici : soit nom == "Mosquée" sans ville, soit nom quelconque sans adresse valide
                var hasJanazas = await _ctx.PrieresJanaza.AnyAsync(p => p.MosqueeId == mosquee.Id);
                if (hasJanazas)
                {
                    mosquee.Statut = "Supprimee";
                    result.Ignores.Add(new(mosquee.Id, mosquee.Adresse, "Janazas liées — désactivée"));
                }
                else
                {
                    result.Supprimes.Add(new(mosquee.Id, mosquee.Adresse, $"Adresse invalide ({mosquee.Nom})"));
                    _ctx.Mosquees.Remove(mosquee);
                }
            }

            // Passe 2 : déduplication — même nom + même adresse → conserver le plus récent
            var entitesActives = mosquees.Where(m =>
                m.Statut != "Supprimee" &&
                _ctx.Entry(m).State != Microsoft.EntityFrameworkCore.EntityState.Deleted
            ).ToList();

            var groupesDoublons = entitesActives
                .Where(m => !string.IsNullOrWhiteSpace(m.Adresse))
                .GroupBy(m => (
                    Nom: (m.Nom ?? "").Trim().ToLowerInvariant(),
                    Adresse: m.Adresse!.Trim().ToLowerInvariant()
                ))
                .Where(g => g.Key.Nom.Length > 0 && g.Count() > 1);

            foreach (var groupe in groupesDoublons)
            {
                var keeper = groupe
                    .OrderByDescending(m => m.DateCreation)
                    .First();

                foreach (var doublon in groupe.Where(m => m.Id != keeper.Id))
                {
                    var hasPrieres = await _ctx.PrieresJanaza.AnyAsync(p => p.MosqueeId == doublon.Id);
                    if (hasPrieres)
                    {
                        doublon.Statut = "Supprimee";
                        result.Ignores.Add(new(doublon.Id, doublon.Adresse, $"Doublon désactivé (gardé #{keeper.Id})"));
                    }
                    else
                    {
                        result.Supprimes.Add(new(doublon.Id, doublon.Adresse, $"Doublon supprimé (gardé #{keeper.Id})"));
                        _ctx.Mosquees.Remove(doublon);
                    }
                }
            }

            if (result.Renommes.Count + result.Supprimes.Count + result.Ignores.Count > 0)
                await _ctx.SaveChangesAsync();

            return result;
        }

        private static DomainModel.Mosquee ToModel(Mosquee e) => new()
        {
            Id = e.Id, Nom = e.Nom, Adresse = e.Adresse,
            Ville = e.Ville, Pays = e.Pays,
            Latitude = e.Latitude, Longitude = e.Longitude,
            OsmId = e.OsmId, Statut = e.Statut, Source = e.Source,
            DateCreation = e.DateCreation, DerniereSyncOsm = e.DerniereSyncOsm,
            UtilisateurId = e.UtilisateurId,
        };

        private static Mosquee ToEntity(DomainModel.Mosquee m) => new()
        {
            Id = m.Id, Nom = m.Nom, Adresse = m.Adresse,
            Ville = m.Ville, Pays = m.Pays,
            Latitude = m.Latitude, Longitude = m.Longitude,
            OsmId = m.OsmId, Statut = m.Statut, Source = m.Source,
            DateCreation = m.DateCreation, DerniereSyncOsm = m.DerniereSyncOsm,
            UtilisateurId = m.UtilisateurId,
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
