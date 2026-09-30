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

        public override void Dispose()
        {
            _timer.Dispose();
            base.Dispose();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await DoWorkAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                HandleCronException(ex);
            }

            while (await _timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DoWorkAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    HandleCronException(ex);
                }
            }
        }

        protected abstract Task DoWorkAsync(CancellationToken cancellationToken);

        private void HandleCronException(Exception ex)
        {
            _logger.LogError(ex, "Cron job {JobName} failed", GetType().Name);
        }
    }
}
