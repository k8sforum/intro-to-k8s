using mytravels.contract.Interfaces;

namespace mytravels.functions;

/// <summary>
/// Applies the SOLR schema at startup. Keeping the schema in code rather than in a mounted configset means
/// there is one definition instead of a bind mount in Compose plus a ConfigMap in each Kubernetes stage,
/// and every stage heals its own collection.
/// </summary>
public class SolrSchemaInitializer : IHostedService
{
    private const int MaxAttempts = 10;

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<SolrSchemaInitializer> _logger;

    public SolrSchemaInitializer(IServiceScopeFactory serviceScopeFactory, ILogger<SolrSchemaInitializer> logger)
    {
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Compose depends_on and Kubernetes readiness probes do not guarantee SOLR has finished creating the
        // collection, so this runs in the background with backoff instead of blocking or crashing the host.
        _ = Task.Run(() => EnsureSchemaWithRetryAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task EnsureSchemaWithRetryAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (cancellationToken.IsCancellationRequested) return;

            try
            {
                using IServiceScope scope = _serviceScopeFactory.CreateScope();
                ISolrIndexService indexService = scope.ServiceProvider.GetRequiredService<ISolrIndexService>();
                await indexService.EnsureSchemaAsync(cancellationToken);

                _logger.LogInformation("SOLR schema ensured on attempt {Attempt}.", attempt);
                return;
            }
            catch (Exception ex)
            {
                TimeSpan delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                _logger.LogWarning(ex, "SOLR schema initialization attempt {Attempt}/{MaxAttempts} failed, retrying in {DelaySeconds}s.",
                    attempt, MaxAttempts, delay.TotalSeconds);

                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        _logger.LogError("SOLR schema could not be initialized after {MaxAttempts} attempts. Searches will fail until SOLR is reachable; POST /api/pointofinterest/reindex once it is.", MaxAttempts);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SOLR schema initializer is stopping.");
        return Task.CompletedTask;
    }
}
