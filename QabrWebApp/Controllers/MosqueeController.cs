using Microsoft.AspNetCore.Mvc;
using QabrWebApp.Builders;
using QabrWebApp.Domain.Models;
using QabrWebApp.Domain.Services;
using QabrWebApp.Request;
using QabrWebApp.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Text;

namespace QabrWebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MosqueeController : ControllerBase
    {
        private readonly IMosqueeService _service;
        private readonly IMosqueeViewModelBuilder _builder;
        private readonly IOverpassService _overpass;
        private readonly IEmailService _email;

        public MosqueeController(IMosqueeService service, IMosqueeViewModelBuilder builder, IOverpassService overpass, IEmailService email)
        {
            _service = service;
            _builder = builder;
            _overpass = overpass;
            _email = email;
        }

        [HttpGet]
        [SwaggerOperation(Summary = "Liste toutes les mosquées")]
        public async Task<IActionResult> GetAll()
        {
            var list = await _service.GetAllAsync();
            return Ok(_builder.BuildList(list));
        }

        [HttpGet("{id}")]
        [SwaggerOperation(Summary = "Récupère une mosquée par ID")]
        public async Task<IActionResult> GetById(int id)
        {
            var m = await _service.GetByIdAsync(id);
            return m is null ? NotFound() : Ok(_builder.Build(m));
        }

        [HttpGet("osm/{osmId}")]
        [SwaggerOperation(Summary = "Récupère une mosquée par OsmId")]
        public async Task<IActionResult> GetByOsmId(string osmId)
        {
            var m = await _service.GetByOsmIdAsync(osmId);
            return m is null ? NotFound() : Ok(_builder.Build(m));
        }

        [HttpGet("nearby")]
        [SwaggerOperation(Summary = "Mosquées proches d'une position depuis le cache DB")]
        public async Task<IActionResult> GetNearby([FromQuery] MosqueeNearbyRequest req)
        {
            var nearby = await _service.GetNearbyAsync(req.Latitude, req.Longitude, req.RadiusKm);
            var vms = nearby
                .Select(m =>
                {
                    var d = HaversineKm(req.Latitude, req.Longitude, m.Latitude, m.Longitude);
                    return _builder.Build(m, Math.Round(d, 1));
                })
                .OrderBy(v => v.DistanceKm)
                .ToList();
            return Ok(vms);
        }

        [HttpPost("sync-osm")]
        [SwaggerOperation(Summary = "Upsert en masse des mosquées OSM dans le cache DB")]
        public async Task<IActionResult> SyncOsm([FromBody] MosqueeSyncOsmRequest req)
        {
            if (req.Mosques.Count == 0) return NoContent();

            var mosquees = req.Mosques
                .Where(m => !string.IsNullOrEmpty(m.OsmId))
                .Select(m => new Mosquee
                {
                    Nom = m.Nom,
                    Adresse = m.Adresse,
                    Latitude = m.Latitude,
                    Longitude = m.Longitude,
                    OsmId = m.OsmId,
                })
                .ToList();

            var suppressedOsmIds = await _service.UpsertBulkFromOsmAsync(mosquees);
            if (suppressedOsmIds.Count > 0)
                return Ok(new { suppressedOsmIds });
            return NoContent();
        }

        [HttpGet("search")]
        [SwaggerOperation(Summary = "Recherche de mosquées par nom ou adresse (pour déclaration)")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return Ok(new List<object>());
            var results = await _service.SearchAsync(q);
            return Ok(_builder.BuildList(results));
        }

        [HttpGet("pending")]
        [SwaggerOperation(Summary = "Mosquées en attente de validation (admin)")]
        public async Task<IActionResult> GetPending()
        {
            var list = await _service.GetPendingAsync();
            return Ok(_builder.BuildList(list));
        }

        [HttpGet("contributions")]
        [SwaggerOperation(Summary = "Mosquées validées ajoutées par les utilisateurs")]
        public async Task<IActionResult> GetContributions()
        {
            var list = await _service.GetContributionsAsync();
            return Ok(_builder.BuildList(list));
        }

        [HttpPost]
        [SwaggerOperation(Summary = "Crée une mosquée manuellement")]
        public async Task<IActionResult> Create([FromBody] MosqueeRequest req)
        {
            var mosquee = new Mosquee
            {
                Nom = req.Nom,
                Adresse = req.Adresse,
                Latitude = req.Latitude,
                Longitude = req.Longitude,
                OsmId = req.OsmId,
            };
            var created = await _service.CreateAsync(mosquee);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, _builder.Build(created));
        }

        [HttpPost("suggestion")]
        [SwaggerOperation(Summary = "Soumet une mosquée pour validation")]
        public async Task<IActionResult> Suggestion([FromBody] MosqueeRequest req)
        {
            var mosquee = new Mosquee
            {
                Nom = req.Nom,
                Adresse = req.Adresse,
                Latitude = req.Latitude,
                Longitude = req.Longitude,
            };
            var created = await _service.CreateSuggestionAsync(mosquee);

            var html = new StringBuilder();
            html.AppendLine("<h2>Nouvelle mosquée soumise par un utilisateur</h2>");
            html.AppendLine($"<p><strong>Nom :</strong> {created.Nom}</p>");
            html.AppendLine($"<p><strong>Adresse :</strong> {created.Adresse ?? "—"}</p>");
            html.AppendLine($"<p><strong>Coordonnées :</strong> {created.Latitude}, {created.Longitude}</p>");
            html.AppendLine($"<p><strong>ID :</strong> {created.Id}</p>");
            html.AppendLine("<p>Connectez-vous à l'interface admin pour valider ou refuser cette mosquée.</p>");

            _ = _email.SendNotificationAsync("support@salatjanaza.org",
                $"[Salat Janaza] Nouvelle mosquée à valider : {created.Nom}", html.ToString());

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, _builder.Build(created));
        }

        [HttpPut("{id}/valider")]
        [SwaggerOperation(Summary = "Valide une mosquée en attente")]
        public async Task<IActionResult> Valider(int id)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();
            await _service.ValiderAsync(id);
            return NoContent();
        }

        [HttpPut("{id}")]
        [SwaggerOperation(Summary = "Met à jour une mosquée")]
        public async Task<IActionResult> Update(int id, [FromBody] MosqueeRequest req)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();
            existing.Nom = req.Nom;
            existing.Adresse = req.Adresse;
            existing.Latitude = req.Latitude;
            existing.Longitude = req.Longitude;
            existing.OsmId = req.OsmId ?? existing.OsmId;
            var updated = await _service.UpdateAsync(existing);
            return Ok(_builder.Build(updated));
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(Summary = "Supprime une mosquée")]
        public async Task<IActionResult> Delete(int id)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();
            await _service.DeleteAsync(id);
            return NoContent();
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
    }
}
