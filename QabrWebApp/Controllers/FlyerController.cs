using Microsoft.AspNetCore.Mvc;
using QabrWebApp.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace QabrWebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FlyerController : ControllerBase
    {
        private readonly IFlyerStorageService _storage;
        private readonly IImportSessionService _sessions;

        private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/png", "image/jpeg", "image/jpg"
        };

        public FlyerController(IFlyerStorageService storage, IImportSessionService sessions)
        {
            _storage = storage;
            _sessions = sessions;
        }

        [HttpPost("upload")]
        [SwaggerOperation(Summary = "Upload d'un flyer janaza vers Google Drive")]
        [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB max
        public async Task<IActionResult> Upload(
            [FromForm] IFormFile file,
            [FromForm] int utilisateurId,
            [FromForm] string? expoPushToken = null)
        {
            if (file is null || file.Length == 0)
                return BadRequest(new { error = "Aucun fichier fourni." });

            if (!AllowedTypes.Contains(file.ContentType))
                return BadRequest(new { error = "Seuls les fichiers PNG et JPG sont acceptés." });

            var session = _sessions.Create(utilisateurId, expoPushToken);
            var ext = Path.GetExtension(file.FileName);
            var driveFileName = $"{session.Token}{ext}";

            try
            {
                await using var stream = file.OpenReadStream();
                await _storage.SaveAsync(stream, driveFileName, file.ContentType);
            }
            catch (Exception ex)
            {
                _sessions.SetError(session.Token, $"Stockage du fichier échoué : {ex.Message}");
                return StatusCode(500, new { error = ex.Message, detail = ex.InnerException?.Message });
            }

            return Ok(new { importToken = session.Token });
        }

        [HttpGet("import-status/{token}")]
        [SwaggerOperation(Summary = "Statut d'une importation de flyer")]
        public IActionResult GetStatus(string token)
        {
            var session = _sessions.Get(token);
            if (session is null)
                return NotFound(new { error = "Session introuvable ou expirée." });

            return Ok(new
            {
                status = session.Status.ToString().ToLower(),
                message = session.Message,
                errorCode = session.ErrorCode,
                priereId = session.PriereId,
                timeUnknown = session.TimeUnknown
            });
        }
    }
}
