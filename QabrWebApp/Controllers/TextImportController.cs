using Microsoft.AspNetCore.Mvc;
using QabrWebApp.Request;
using QabrWebApp.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace QabrWebApp.Controllers
{
    [ApiController]
    [Route("api/admin/[controller]")]
    public class TextImportController : ControllerBase
    {
        private readonly ITextImportStorageService _storage;
        private readonly ITextImportSummaryService _summary;
        private readonly IConfiguration _config;

        public TextImportController(
            ITextImportStorageService storage,
            ITextImportSummaryService summary,
            IConfiguration config)
        {
            _storage = storage;
            _summary = summary;
            _config = config;
        }

        [HttpPost]
        [SwaggerOperation(Summary = "Sauvegarde du texte brut (prières funéraires) en .txt sur Google Drive")]
        public async Task<IActionResult> Save([FromBody] TextImportRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Text))
                return BadRequest(new { error = "Le texte est vide." });

            var token = Guid.NewGuid().ToString("N");
            var fileName = $"{token}.txt";

            try
            {
                var url = await _storage.SaveAsync(req.Text, fileName);
                return Ok(new { url, filename = fileName, token });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Appelé par n8n à la fin du workflow pour signaler que l'import est terminé
        [HttpPost("{token}/summary")]
        [SwaggerOperation(Summary = "Finalise le résumé d'importation (appelé par l'agent IA en fin de workflow, corps vide)")]
        public IActionResult SaveSummary(string token)
        {
            var providedKey = Request.Headers["X-Import-Key"].FirstOrDefault();
            var expectedKey = _config["ImportApiKey"];
            if (string.IsNullOrEmpty(expectedKey) || providedKey != expectedKey)
                return Unauthorized(new { error = "Clé API invalide." });

            _summary.Finalize(token);
            return Ok();
        }

        // Polled par le mobile/web pour savoir si l'import est terminé
        [HttpGet("{token}/summary")]
        [SwaggerOperation(Summary = "Récupère le résumé d'importation pour un token donné")]
        public IActionResult GetSummary(string token)
        {
            var s = _summary.Get(token);
            if (s is null || !s.Ready) return NotFound(new { ready = false });

            return Ok(new
            {
                ready = true,
                total = s.Total,
                success = s.Success,
                skipped = s.Skipped,
                skippedEntries = s.SkippedEntries.Select(e => new
                {
                    mosqueeNom = e.MosqueeNom,
                    nomDefunt = e.NomDefunt,
                    dateHeurePriere = e.DateHeurePriere,
                    reason = e.Reason,
                }),
            });
        }
    }
}
