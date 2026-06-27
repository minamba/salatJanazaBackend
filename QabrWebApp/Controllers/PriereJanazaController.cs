using Microsoft.AspNetCore.Mvc;
using QabrWebApp.Builders;
using QabrWebApp.Domain.Models;
using QabrWebApp.Domain.Services;
using QabrWebApp.Request;
using QabrWebApp.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace QabrWebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PriereJanazaController : ControllerBase
    {
        private readonly IPriereJanazaService _service;
        private readonly IPriereJanazaViewModelBuilder _builder;
        private readonly IPushNotificationService _push;

        public PriereJanazaController(
            IPriereJanazaService service,
            IPriereJanazaViewModelBuilder builder,
            IPushNotificationService push)
        {
            _service = service;
            _builder = builder;
            _push = push;
        }

        [HttpGet]
        [SwaggerOperation(Summary = "Liste toutes les prières")]
        public async Task<IActionResult> GetAll()
        {
            var list = await _service.GetAllAsync();
            return Ok(_builder.BuildList(list));
        }

        [HttpGet("upcoming")]
        [SwaggerOperation(Summary = "Prières à venir ou en cours")]
        public async Task<IActionResult> GetUpcoming()
        {
            var list = await _service.GetUpcomingAsync();
            return Ok(_builder.BuildList(list));
        }

        [HttpGet("{id}")]
        [SwaggerOperation(Summary = "Récupère une prière par ID")]
        public async Task<IActionResult> GetById(int id)
        {
            var p = await _service.GetByIdAsync(id);
            return p is null ? NotFound() : Ok(_builder.Build(p));
        }

        [HttpGet("mosquee/{mosqueeId}")]
        [SwaggerOperation(Summary = "Prières d'une mosquée")]
        public async Task<IActionResult> GetByMosquee(int mosqueeId)
        {
            var list = await _service.GetByMosqueeIdAsync(mosqueeId);
            return Ok(_builder.BuildList(list));
        }

        [HttpGet("utilisateur/{utilisateurId}")]
        [SwaggerOperation(Summary = "Déclarations d'un utilisateur")]
        public async Task<IActionResult> GetByUtilisateur(int utilisateurId)
        {
            var list = await _service.GetByUtilisateurIdAsync(utilisateurId);
            return Ok(_builder.BuildList(list));
        }

        [HttpPost]
        [SwaggerOperation(Summary = "Déclare une prière funéraire et notifie les abonnés")]
        public async Task<IActionResult> Create([FromBody] PriereJanazaRequest req)
        {
            var priere = new PriereJanaza
            {
                MosqueeId = req.MosqueeId,
                UtilisateurId = req.UtilisateurId,
                NomDefunt = req.NomDefunt,
                EstAnonyme = req.EstAnonyme,
                Genre = req.Genre,
                DateHeurePriere = req.DateHeurePriere,
                Commentaire = req.Commentaire,
                PaysEnterrement = req.PaysEnterrement,
                VilleEnterrement = req.VilleEnterrement,
                AnneeNaissance = req.AnneeNaissance,
                AnneeDeces = req.AnneeDeces,
                UtcOffsetMinutes = req.UtcOffsetMinutes,
            };

            var created = await _service.CreateAsync(priere);
            await _push.NotifyMosqueeSubscribersAsync(req.MosqueeId, created);
            await _push.ScheduleMosqueeReminderAsync(req.MosqueeId, created);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, _builder.Build(created));
        }

        [HttpPut("{id}")]
        [SwaggerOperation(Summary = "Met à jour une prière")]
        public async Task<IActionResult> Update(int id, [FromBody] PriereJanazaRequest req)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();
            existing.MosqueeId = req.MosqueeId;
            existing.NomDefunt = req.NomDefunt;
            existing.EstAnonyme = req.EstAnonyme;
            existing.Genre = req.Genre;
            existing.DateHeurePriere = req.DateHeurePriere;
            existing.Commentaire = req.Commentaire;
            existing.PaysEnterrement = req.PaysEnterrement;
            existing.VilleEnterrement = req.VilleEnterrement;
            existing.AnneeNaissance = req.AnneeNaissance;
            existing.AnneeDeces = req.AnneeDeces;
            var updated = await _service.UpdateAsync(existing);
            return Ok(_builder.Build(updated));
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(Summary = "Supprime une prière")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var existing = await _service.GetByIdAsync(id);
                if (existing is null) return NotFound();
                await _service.DeleteAsync(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, inner = ex.InnerException?.Message, type = ex.GetType().Name });
            }
        }
    }
}
