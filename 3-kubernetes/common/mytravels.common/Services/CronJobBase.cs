using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace mytravels.common.Services
{
    public abstract class CronJobBase : BackgroundService
    {
        protected readonly ILogger<CronJobBase> _logger;
        protected readonly PeriodicTimer _timer;

        protected CronJobBase(ILogger<CronJobBase> logger, TimeSpan timeSpan)
        {
            _logger = logger;
            _timer = new PeriodicTimer(timeSpan);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await DoWorkAsync();
            while (await _timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DoWorkAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing timed work");
                }
            }
        }
        protected abstract Task DoWorkAsync();
    }
}
