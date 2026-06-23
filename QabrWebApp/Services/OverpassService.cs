using QabrWebApp.Domain.Models;
using System.Text.Json;

namespace QabrWebApp.Services
{
    public class OverpassService : IOverpassService
    {
        private readonly HttpClient _http;
        private readonly ILogger<OverpassService> _logger;

        public OverpassService(HttpClient http, ILogger<OverpassService> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<List<Mosquee>> FetchMosqueesAsync(double latitude, double longitude, double radiusKm)
        {
            var radiusM = (int)(radiusKm * 1000);
            var query = $"""
                [out:json][timeout:25];
                (
                  node["amenity"="place_of_worship"]["religion"="muslim"](around:{radiusM},{latitude},{longitude});
                  way["amenity"="place_of_worship"]["religion"="muslim"](around:{radiusM},{latitude},{longitude});
                  relation["amenity"="place_of_worship"]["religion"="muslim"](around:{radiusM},{latitude},{longitude});
                );
                out center;
                """;

            try
            {
                var response = await _http.PostAsync(
                    "https://overpass-api.de/api/interpreter",
                    new StringContent(query));

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Overpass API returned {StatusCode}", response.StatusCode);
                    return [];
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                var mosquees = new List<Mosquee>();
                foreach (var element in doc.RootElement.GetProperty("elements").EnumerateArray())
                {
                    var osmId = $"{element.GetProperty("type").GetString()}_{element.GetProperty("id").GetInt64()}";
                    var tags = element.TryGetProperty("tags", out var t) ? t : (JsonElement?)null;
                    var nom = tags?.TryGetProperty("name", out var n) == true ? n.GetString() : null;

                    double lat, lon;
                    if (element.TryGetProperty("lat", out var latEl))
                    {
                        lat = latEl.GetDouble();
                        lon = element.GetProperty("lon").GetDouble();
                    }
                    else if (element.TryGetProperty("center", out var center))
                    {
                        lat = center.GetProperty("lat").GetDouble();
                        lon = center.GetProperty("lon").GetDouble();
                    }
                    else continue;

                    mosquees.Add(new Mosquee
                    {
                        Nom = nom ?? "Mosquée",
                        Adresse = BuildAdresse(tags),
                        Latitude = lat,
                        Longitude = lon,
                        OsmId = osmId,
                    });
                }

                return mosquees;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'appel Overpass API");
                return [];
            }
        }

        private static string? BuildAdresse(JsonElement? tags)
        {
            if (tags is null) return null;
            var parts = new List<string>();
            if (tags.Value.TryGetProperty("addr:housenumber", out var num)) parts.Add(num.GetString()!);
            if (tags.Value.TryGetProperty("addr:street", out var street)) parts.Add(street.GetString()!);
            if (tags.Value.TryGetProperty("addr:postcode", out var pc)) parts.Add(pc.GetString()!);
            if (tags.Value.TryGetProperty("addr:city", out var city)) parts.Add(city.GetString()!);
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }
    }
}
