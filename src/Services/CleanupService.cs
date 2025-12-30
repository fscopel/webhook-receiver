namespace WebhookReceiver.Services;

public class CleanupService : BackgroundService
{
    private readonly WebhookStore _store;
    private readonly ILogger<CleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public CleanupService(WebhookStore store, ILogger<CleanupService> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cleanup service started. Running every {Interval}", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_interval, stoppingToken);

            try
            {
                var removed = _store.CleanupExpired();
                if (removed > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} expired webhook entries", removed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup");
            }
        }
    }
}

