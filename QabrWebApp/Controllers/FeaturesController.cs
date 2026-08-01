using Microsoft.AspNetCore.Mvc;
using QabrWebApp.Services;

namespace QabrWebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FeaturesController : ControllerBase
    {
        private readonly FeatureFlagsService _flags;

        public FeaturesController(FeatureFlagsService flags) => _flags = flags;

        [HttpGet]
        public IActionResult Get() => Ok(_flags.Get());

        [HttpPut("donation-button")]
        public async Task<IActionResult> SetDonationButton([FromBody] SetDonationButtonRequest req)
        {
            var updated = await _flags.SetDonationButtonAsync(req.Visible);
            return Ok(updated);
        }
    }

    public record SetDonationButtonRequest(bool Visible);
}
