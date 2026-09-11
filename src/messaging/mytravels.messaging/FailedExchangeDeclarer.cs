using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using mytravels.contract.Constants;

namespace mytravels.functions;

public class FailedExchangeDeclarer : IHostedService
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<FailedExchangeDeclarer> _logger;

    public FailedExchangeDeclarer(IConnectionFactory connectionFactory, ILogger<FailedExchangeDeclarer> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Declaring failed exchanges...");

        IConnection connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        // Declare all failed exchanges (fanout, non-durable, auto-delete)
        await channel.ExchangeDeclareAsync(
            exchange: ExchangeNames.ResizeImageFailed,
            type: ExchangeType.Fanout,
            durable: false,
            autoDelete: true,
            cancellationToken: cancellationToken);
        _logger.LogInformation("Failed exchange '{FailedExchange}' declared.", ExchangeNames.ResizeImageFailed);

        await channel.ExchangeDeclareAsync(
            exchange: ExchangeNames.AppendFormattedAddressFailed,
            type: ExchangeType.Fanout,
            durable: false,
            autoDelete: true,
            cancellationToken: cancellationToken);
        _logger.LogInformation("Failed exchange '{FailedExchange}' declared.", ExchangeNames.AppendFormattedAddressFailed);

        await channel.ExchangeDeclareAsync(
            exchange: ExchangeNames.AppendImageTagsFailed,
            type: ExchangeType.Fanout,
            durable: false,
            autoDelete: true,
            cancellationToken: cancellationToken);
        _logger.LogInformation("Failed exchange '{FailedExchange}' declared.", ExchangeNames.AppendImageTagsFailed);

        await channel.CloseAsync(cancellationToken);
        await connection.CloseAsync(cancellationToken);

        _logger.LogInformation("All failed exchanges declared successfully.");
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Failed exchange declarer is stopping.");
        return Task.CompletedTask;
    }
}
