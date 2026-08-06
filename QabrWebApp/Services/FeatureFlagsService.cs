using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QabrWebApp.Dal.Entities;

namespace QabrWebApp.Services
{
    public class FeatureFlagsDto
    {
        public bool DonationButtonVisible { get; set; } = true;
    }

    public class FeatureFlagsService
    {
        private const string KEY_DONATION = "donationButtonVisible";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private FeatureFlagsDto _cache = new();

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
                var setting = await db.AppSettings.FindAsync(KEY_DONATION);
                if (setting != null && bool.TryParse(setting.Value, out var val))
                    _cache.DonationButtonVisible = val;
            }
            finally { _lock.Release(); }
        }

        public FeatureFlagsDto Get() => _cache;

        public async Task<FeatureFlagsDto> SetDonationButtonAsync(bool visible)
        {
            await _lock.WaitAsync();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<QabrWebAppDatabaseContext>();
                var setting = await db.AppSettings.FindAsync(KEY_DONATION);
                if (setting == null)
                {
                    db.AppSettings.Add(new AppSetting { Key = KEY_DONATION, Value = visible.ToString() });
                }
                else
                {
                    setting.Value = visible.ToString();
                }
                await db.SaveChangesAsync();
                _cache.DonationButtonVisible = visible;
                return _cache;
            }
            finally { _lock.Release(); }
        }
    }
}
