using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QabrWebApp.Dal.Entities;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace QabrWebApp.Services
{
    public class InfoMessageDto
    {
        public bool Active { get; set; }
        public string Message { get; set; } = "";
    }

    public class FeatureFlagsDto
    {
        public bool DonationButtonVisible { get; set; } = true;
        public InfoMessageDto InfoMessage { get; set; } = new();
    }

    public class FeatureFlagsService
    {
        private const string KEY_DONATION    = "donationButtonVisible";
        private const string KEY_INFO_ACTIVE = "infoMessageActive";
        private const string KEY_INFO_TEXT   = "infoMessageText";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private FeatureFlagsDto _cache = new();
        private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();

        public FeatureFlagsService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task InitAsync()
        {
            await _lock.WaitAsync();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<QabrWebAppDatabaseContext>();

                var donation = await db.AppSettings.FindAsync(KEY_DONATION);
                if (donation != null && bool.TryParse(donation.Value, out var donVal))
                    _cache.DonationButtonVisible = donVal;

                var infoActive = await db.AppSettings.FindAsync(KEY_INFO_ACTIVE);
                if (infoActive != null && bool.TryParse(infoActive.Value, out var activeVal))
                    _cache.InfoMessage.Active = activeVal;

                var infoText = await db.AppSettings.FindAsync(KEY_INFO_TEXT);
                if (infoText != null)
                    _cache.InfoMessage.Message = infoText.Value ?? "";
            }
            finally { _lock.Release(); }
        }

        public FeatureFlagsDto Get() => _cache;

        public void RegisterSocket(string id, WebSocket socket) => _sockets.TryAdd(id, socket);
        public void UnregisterSocket(string id) => _sockets.TryRemove(id, out _);

        private async Task BroadcastAsync()
        {
            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            foreach (var (id, socket) in _sockets)
            {
                if (socket.State != WebSocketState.Open) { _sockets.TryRemove(id, out _); continue; }
                try { await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None); }
                catch { _sockets.TryRemove(id, out _); }
            }
        }

        public async Task<FeatureFlagsDto> SetDonationButtonAsync(bool visible)
        {
            await _lock.WaitAsync();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<QabrWebAppDatabaseContext>();
                var setting = await db.AppSettings.FindAsync(KEY_DONATION);
                if (setting == null)
                    db.AppSettings.Add(new AppSetting { Key = KEY_DONATION, Value = visible.ToString() });
                else
                    setting.Value = visible.ToString();
                await db.SaveChangesAsync();
                _cache.DonationButtonVisible = visible;
            }
            finally { _lock.Release(); }
            await BroadcastAsync();
            return _cache;
        }

        public async Task<FeatureFlagsDto> SetInfoMessageAsync(bool active, string message)
        {
            bool wasActive = _cache.InfoMessage.Active;

            await _lock.WaitAsync();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<QabrWebAppDatabaseContext>();

                var activeRow = await db.AppSettings.FindAsync(KEY_INFO_ACTIVE);
                if (activeRow == null)
                    db.AppSettings.Add(new AppSetting { Key = KEY_INFO_ACTIVE, Value = active.ToString() });
                else
                    activeRow.Value = active.ToString();

                var textRow = await db.AppSettings.FindAsync(KEY_INFO_TEXT);
                if (textRow == null)
                    db.AppSettings.Add(new AppSetting { Key = KEY_INFO_TEXT, Value = message });
                else
                    textRow.Value = message;

                await db.SaveChangesAsync();
                _cache.InfoMessage.Active  = active;
                _cache.InfoMessage.Message = message;

            }
            finally { _lock.Release(); }
            await BroadcastAsync();

            if (active && !wasActive)
            {
                _ = Task.Run(async () =>
                {
                    using var pushScope = _scopeFactory.CreateScope();
                    var push = pushScope.ServiceProvider.GetRequiredService<IPushNotificationService>();
                    await push.SendToAllUsersAsync(
                        "📣 Information importante",
                        "Un nouveau message est disponible dans l'application.\n─────────────────\nL'équipe Salat Janaza");
                });
            }

            return _cache;
        }
    }
}
