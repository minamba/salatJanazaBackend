using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace QabrWebApp.Services
{
    public class CommentWebSocketManager
    {
        private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();
        private readonly ILogger<CommentWebSocketManager> _logger;

        public CommentWebSocketManager(ILogger<CommentWebSocketManager> logger)
        {
            _logger = logger;
        }

        public string Add(WebSocket socket)
        {
            var id = Guid.NewGuid().ToString("N");
            _sockets[id] = socket;
            _logger.LogDebug("WS comment: client {Id} connecté ({Count} total)", id, _sockets.Count);
            return id;
        }

        public void Remove(string id)
        {
            _sockets.TryRemove(id, out _);
            _logger.LogDebug("WS comment: client {Id} déconnecté ({Count} restants)", id, _sockets.Count);
        }

        public async Task BroadcastAsync(string json)
        {
            var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));
            var dead = new List<string>();

            foreach (var (id, ws) in _sockets)
            {
                if (ws.State != WebSocketState.Open) { dead.Add(id); continue; }
                try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
                catch { dead.Add(id); }
            }

            foreach (var id in dead) Remove(id);
        }
    }
}
