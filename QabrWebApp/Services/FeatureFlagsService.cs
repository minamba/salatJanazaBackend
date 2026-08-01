using System.Text.Json;

namespace QabrWebApp.Services
{
    public class FeatureFlagsDto
    {
        public bool DonationButtonVisible { get; set; } = true;
    }

    public class FeatureFlagsService
    {
        private readonly string _filePath;
        private FeatureFlagsDto _flags;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public FeatureFlagsService(IWebHostEnvironment env)
        {
            _filePath = Path.Combine(env.ContentRootPath, "features.json");
            _flags = Load();
        }

        private FeatureFlagsDto Load()
        {
            if (!File.Exists(_filePath)) return new FeatureFlagsDto();
            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<FeatureFlagsDto>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new FeatureFlagsDto();
            }
            catch { return new FeatureFlagsDto(); }
        }

        public FeatureFlagsDto Get() => _flags;

        public async Task<FeatureFlagsDto> SetDonationButtonAsync(bool visible)
        {
            await _lock.WaitAsync();
            try
            {
                _flags.DonationButtonVisible = visible;
                var json = JsonSerializer.Serialize(_flags, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_filePath, json);
                return _flags;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
