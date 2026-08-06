using System.Text.Json;
using System.Text.Json.Serialization;

namespace QabrWebApp.Services
{
    /// <summary>Ville et pays d'un point, ou null si le service ne sait pas.</summary>
    public record Lieu(string? Ville, string? Pays);

    public interface IGeocodageInverseService
    {
        Task<Lieu?> ResoudreAsync(double latitude, double longitude, CancellationToken ct = default);
    }

    /// <summary>
    /// Géocodage INVERSE : des coordonnées vers une ville et un pays.
    ///
    /// POURQUOI L'INVERSE ET NON L'ADRESSE
    /// -----------------------------------
    /// Chaque mosquée porte déjà sa latitude et sa longitude. Partir de
    /// l'adresse reviendrait à analyser du texte libre saisi à la main, où
    /// « Rue Morand, 75011 Paris » et « …, Évry, Essonne, 91000, France »
    /// cohabitent — deux formats inconciliables, sans compter les adresses
    /// sans virgule ni code postal. Les coordonnées, elles, sont sans
    /// ambiguïté : un point sur Terre est dans une ville et une seule.
    ///
    /// LA CADENCE N'EST PAS UN RÉGLAGE DE CONFORT
    /// ------------------------------------------
    /// Nominatim est gratuit et sa politique d'usage impose une requête par
    /// seconde et un User-Agent identifiable. Dépasser fait bannir l'adresse
    /// IP du serveur — ce qui casserait aussi la déclaration de prière, qui
    /// dépend du même service. D'où le verrou partagé plus bas : même si dix
    /// appels arrivent en même temps, ils passent l'un après l'autre.
    /// </summary>
    public class GeocodageInverseService : IGeocodageInverseService
    {
        private readonly HttpClient _http;
        private readonly ILogger<GeocodageInverseService> _logger;

        // Statique et non par instance : le service est enregistré en Scoped,
        // donc une instance par requête. Un verrou d'instance ne cadencerait
        // rien du tout.
        private static readonly SemaphoreSlim Jeton = new(1, 1);
        private static DateTime _dernierAppel = DateTime.MinValue;
        private static readonly TimeSpan Cadence = TimeSpan.FromMilliseconds(1100);

        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

        public GeocodageInverseService(HttpClient http, ILogger<GeocodageInverseService> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<Lieu?> ResoudreAsync(double latitude, double longitude, CancellationToken ct = default)
        {
            await Jeton.WaitAsync(ct);
            try
            {
                var attente = Cadence - (DateTime.UtcNow - _dernierAppel);
                if (attente > TimeSpan.Zero) await Task.Delay(attente, ct);
                _dernierAppel = DateTime.UtcNow;

                // zoom=10 vise l'échelle de la ville. Plus fin, Nominatim rend
                // le quartier ou la rue ; plus grossier, le département.
                var url = "https://nominatim.openstreetmap.org/reverse"
                          + $"?lat={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                          + $"&lon={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                          + "&format=json&zoom=10&addressdetails=1&accept-language=fr";

                var reponse = await _http.GetAsync(url, ct);
                if (!reponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[Geocodage] Nominatim a répondu {Code} pour {Lat},{Lon}",
                        reponse.StatusCode, latitude, longitude);
                    return null;
                }

                var resultat = JsonSerializer.Deserialize<ReponseNominatim>(
                    await reponse.Content.ReadAsStringAsync(ct), Json);

                var a = resultat?.Address;
                if (a is null) return null;

                // Nominatim n'emploie pas le même champ selon la taille de la
                // commune : `city` pour une grande ville, `town` pour une
                // moyenne, `village` pour un bourg. Prendre uniquement `city`
                // laisserait vides toutes les petites communes — c'est-à-dire
                // la majorité des mosquées de province.
                var ville = a.City ?? a.Town ?? a.Village ?? a.Municipality ?? a.Suburb;

                return new Lieu(Nettoyer(ville), Nettoyer(a.Country));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Geocodage] Échec pour {Lat},{Lon}", latitude, longitude);
                return null;
            }
            finally
            {
                Jeton.Release();
            }
        }

        private static string? Nettoyer(string? valeur)
        {
            var v = valeur?.Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }

        private class ReponseNominatim
        {
            public AdresseNominatim? Address { get; set; }

            public class AdresseNominatim
            {
                public string? City { get; set; }
                public string? Town { get; set; }
                public string? Village { get; set; }
                public string? Municipality { get; set; }
                public string? Suburb { get; set; }
                public string? Country { get; set; }

                [JsonPropertyName("country_code")]
                public string? CountryCode { get; set; }
            }
        }
    }
}
