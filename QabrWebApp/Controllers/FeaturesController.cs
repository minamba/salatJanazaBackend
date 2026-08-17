using Microsoft.AspNetCore.Mvc;
using QabrWebApp.Services;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

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

        [HttpPut("info-message")]
        public async Task<IActionResult> SetInfoMessage([FromBody] SetInfoMessageRequest req)
        {
            var updated = await _flags.SetInfoMessageAsync(req.Active, req.Message ?? "");
            return Ok(updated);
        }

        [HttpGet("ws")]
        public async Task HandleWebSocket()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = 400;
                return;
            }

            var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            var id = Guid.NewGuid().ToString();
            _flags.RegisterSocket(id, socket);

            // Envoie l'état actuel immédiatement à la connexion
            var json = JsonSerializer.Serialize(_flags.Get(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

            // Garde la connexion ouverte jusqu'à déconnexion du client
            var buffer = new byte[4];
            try
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                while (!result.CloseStatus.HasValue)
                    result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                await socket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
            }
            catch { }
            finally { _flags.UnregisterSocket(id); }
        }
    }

    public record SetDonationButtonRequest(bool Visible);
    public record SetInfoMessageRequest(bool Active, string? Message);
}
