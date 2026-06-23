using Microsoft.AspNetCore.Mvc;
using QabrWebApp.Builders;
using QabrWebApp.Domain.Models;
using QabrWebApp.Domain.Services;
using QabrWebApp.Request;
using Swashbuckle.AspNetCore.Annotations;

namespace QabrWebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AbonnementController : ControllerBase
    {
        private readonly IAbonnementService _service;
        private readonly IAbonnementViewModelBuilder _builder;

        public AbonnementController(IAbonnementService service, IAbonnementViewModelBuilder builder)
        {
            _service = service;
            _builder = builder;
        }

        [HttpGet("utilisateur/{utilisateurId}")]
        [SwaggerOperation(Summary = "Liste les abonnements d'un utilisateur")]
        public async Task<IActionResult> GetByUtilisateur(int utilisateurId)
        {
            var list = await _service.GetByUtilisateurIdAsync(utilisateurId);
            return Ok(_builder.BuildList(list));
        }

        [HttpGet("{id}")]
        [SwaggerOperation(Summary = "Récupère un abonnement par ID")]
        public async Task<IActionResult> GetById(int id)
        {
            var a = await _service.GetByIdAsync(id);
            return a is null ? NotFound() : Ok(_builder.Build(a));
        }

        [HttpPost]
        [SwaggerOperation(Summary = "Abonne un utilisateur à une mosquée")]
        public async Task<IActionResult> Subscribe([FromBody] AbonnementRequest req)
        {
            if (await _service.ExistsAsync(req.UtilisateurId, req.MosqueeId))
                return Conflict(new { message = "Déjà abonné à cette mosquée." });

            var abonnement = new Abonnement
            {
                UtilisateurId = req.UtilisateurId,
                MosqueeId = req.MosqueeId,
            };
            var created = await _service.CreateAsync(abonnement);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, _builder.Build(created));
        }

        [HttpPut("{id}/notif")]
        [SwaggerOperation(Summary = "Active ou désactive les notifications pour un abonnement")]
        public async Task<IActionResult> ToggleNotif(int id, [FromBody] AbonnementToggleRequest req)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();
            existing.NotifActive = req.NotifActive;
            var updated = await _service.UpdateAsync(existing);
            return Ok(_builder.Build(updated));
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(Summary = "Désabonne d'une mosquée")]
        public async Task<IActionResult> Unsubscribe(int id)
        {
            var existing = await _service.GetByIdAsync(id);
            if (existing is null) return NotFound();
            await _service.DeleteAsync(id);
            return NoContent();
        }
    }
}
