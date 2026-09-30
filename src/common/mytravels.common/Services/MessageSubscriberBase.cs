using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Text;
using mytravels.contract.Constants;
using mytravels.contract.Entities;
using mytravels.contract.Interfaces;
namespace mytravels.common.Services;

public abstract class MessageSubscriberBase<T> : IHostedService where T : IMessage
{
    private static readonly ActivitySource _activitySource = new("MyTravels.RabbitMQ");

    protected readonly ILogger<MessageSubscriberBase<T>> _logger;
    protected readonly IConfiguration _configuration;
    private readonly string _exchangeName;
    private readonly string _queueName;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    protected readonly ConnectionFactory _factory;
    private readonly CancellationTokenSource _stopping = new();
    private IConnection _connection;
    private IChannel _channel;

    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(2);

    protected MessageSubscriberBase(ILogger<MessageSubscriberBase<T>> logger, IConfiguration configuration, string exchangeName, string queueName, IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _exchangeName = exchangeName ?? throw new ArgumentNullException(nameof(exchangeName));
        _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));

        string uri = _configuration.GetValue<string>("RabbitMQ:Uri")
                      ?? throw new InvalidOperationException("RabbitMQ URI not configured.");

        _factory = new ConnectionFactory
        {
            Uri = new Uri(uri),
            ClientProvidedName = queueName
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("{Service} is starting.", nameof(MessageSubscriberBase<T>));

        _connection = await _factory.CreateConnectionAsync(cancellationToken);
        _logger.LogInformation("Connected to RabbitMQ at {Uri}", _factory.Uri);

        IChannel channel = _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        _logger.LogInformation("Channel created.");

        await channel.ExchangeDeclareAsync(exchange: _exchangeName, type: ExchangeType.Fanout, cancellationToken: cancellationToken);
        _logger.LogInformation("Exchange '{Exchange}' declared.", _exchangeName);

        await channel.QueueDeclareAsync(queue: _queueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);

        await channel.QueueBindAsync(queue: _queueName, exchange: _exchangeName, routingKey: string.Empty, cancellationToken: cancellationToken);
        _logger.LogInformation("Queue '{Queue}' bound to exchange '{Exchange}'.", _queueName, _exchangeName);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);

        // Handlers run for the life of the consumer, so they observe the stop token rather than the start-up one.
        consumer.ReceivedAsync += async (model, ea) => await ReceivedAsync(model, ea, channel, _stopping.Token);

        await channel.BasicConsumeAsync(_queueName, autoAck: false, consumer: consumer, cancellationToken: cancellationToken);
        _logger.LogInformation("Consuming messages from queue '{Queue}'.", _queueName);
    }

    protected abstract Task ProcessMessageAsync(T obj, CancellationToken cancellationToken);

    private async Task ReceivedAsync(object _, BasicDeliverEventArgs ea, IChannel channel, CancellationToken cancellationToken)
    {
        // Extract trace context from headers
        var traceparent = null as string;
        if (ea.BasicProperties?.Headers != null && ea.BasicProperties.Headers.TryGetValue("traceparent", out var tp))
        {
            traceparent = tp?.ToString();
        }

        // Start linked Consumer Activity
        ActivityContext linkedContext = default;
        if (!string.IsNullOrEmpty(traceparent))
        {
            ActivityContext.TryParse(traceparent, null, out linkedContext);
        }

        using (var activity = _activitySource.StartActivity(
            $"{ea.Exchange} consume",
            ActivityKind.Consumer,
            linkedContext))
        {
            T message = default(T);
            var retryCount = 0;

            try
            {
                // Read retry count from headers
                if (ea.BasicProperties?.Headers != null && ea.BasicProperties.Headers.TryGetValue("x-retry-count", out var rc))
                {
                    if (int.TryParse(rc?.ToString(), out var count))
                    {
                        retryCount = count;
                    }
                }

                // Deserialize message
                byte[] body = ea.Body.ToArray();
                var messageJson = Encoding.UTF8.GetString(body);
                message = JsonConvert.DeserializeObject<T>(messageJson);

                if (Equals(message, default(T)))
                {
                    _logger.LogWarning("Received null message from {Exchange}.", ea.Exchange);
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken);
                    return;
                }

                // Process the message
                await ProcessMessageAsync(message, cancellationToken);

                // Success: acknowledge
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken);

                await LogAuditEventAsync(message.CorrelationId, message.AuditPointOfInterestId, ea.Exchange, MessageAuditEventTypes.ConsumeSucceeded, retryCount, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutting down mid-message: hand it back untouched rather than burning a retry on it.
                await TryRequeueAsync(channel, ea.DeliveryTag);
            }
            catch (Exception ex)
            {
                Guid? correlationId = message?.CorrelationId;
                int? pointOfInterestId = message?.AuditPointOfInterestId;

                _logger.LogError(ex, "Error processing message from {Exchange}, retry count {RetryCount}, PointOfInterestId {PointOfInterestId}, CorrelationId {CorrelationId}",
                    ea.Exchange, retryCount, pointOfInterestId, correlationId);

                // Check retry threshold
                if (retryCount < MaxRetries)
                {
                    // Increment retry count header and republish to same exchange
                    var properties = new BasicProperties();
                    properties.Headers ??= new Dictionary<string, object>();
                    properties.Headers["x-retry-count"] = retryCount + 1;

                    // Copy other headers from original
                    if (ea.BasicProperties?.Headers != null)
                    {
                        foreach (var kvp in ea.BasicProperties.Headers)
                        {
                            if (kvp.Key != "x-retry-count")
                            {
                                properties.Headers[kvp.Key] = kvp.Value;
                            }
                        }
                    }

                    // Back off before republishing, holding the original unacked: a failure that needs time to clear
                    // (rate limit, provider outage) would otherwise burn every retry in milliseconds, and a crash
                    // during the wait redelivers the original with its retry count intact. The cost is that this
                    // queue's consumer is paused for the delay.
                    await Task.Delay(RetryBaseDelay * Math.Pow(2, retryCount), cancellationToken);

                    // Republish message with incremented retry count
                    await channel.BasicPublishAsync(
                        exchange: ea.Exchange,
                        routingKey: "",
                        mandatory: false,
                        basicProperties: properties,
                        body: ea.Body,
                        cancellationToken: cancellationToken
                    );

                    // Acknowledge original message
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken);
                    _logger.LogWarning("Message republished for retry (attempt {RetryCount}/{MaxRetries}) for {Exchange}, PointOfInterestId {PointOfInterestId}, CorrelationId {CorrelationId}",
                        retryCount + 1, MaxRetries, ea.Exchange, pointOfInterestId, correlationId);
                    await LogAuditEventAsync(correlationId, pointOfInterestId, ea.Exchange, MessageAuditEventTypes.Retried, retryCount + 1, ex.Message);
                }
                else
                {
                    // Failed after 3 attempts: persist the payload so it can be inspected/retried from the admin page
                    await SaveFailedMessageAsync(correlationId, pointOfInterestId, ea.Exchange, ea.Body.ToArray(), ex.Message, retryCount);

                    // Acknowledge original message so it stops retrying
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken);
                    _logger.LogError("Message dead-lettered after {MaxRetries} attempts on {Exchange}, PointOfInterestId {PointOfInterestId}, CorrelationId {CorrelationId}",
                        MaxRetries, ea.Exchange, pointOfInterestId, correlationId);
                    await LogAuditEventAsync(correlationId, pointOfInterestId, ea.Exchange, MessageAuditEventTypes.Failed, retryCount, ex.Message);
                }
            }
        }
    }

    private async Task SaveFailedMessageAsync(Guid? correlationId, int? pointOfInterestId, string exchange, byte[] body, string errorMessage, int retryCount)
    {
        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            IFailedMessageWriter writer = scope.ServiceProvider.GetRequiredService<IFailedMessageWriter>();
            await writer.SaveAsync(new FailedMessage
            {
                CorrelationId = correlationId ?? Guid.Empty,
                PointOfInterestId = pointOfInterestId,
                OriginalExchange = exchange,
                Payload = Encoding.UTF8.GetString(body),
                ErrorMessage = errorMessage,
                RetryCount = retryCount,
                FailedAt = DateTime.UtcNow
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist failed-message record for {Exchange}, correlation {CorrelationId}", exchange, correlationId);
        }
    }

    private async Task LogAuditEventAsync(Guid? correlationId, int? pointOfInterestId, string exchange, string eventType, int retryCount, string errorMessage)
    {
        if (!correlationId.HasValue || correlationId == Guid.Empty) return;

        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            IMessageAuditLogger auditLogger = scope.ServiceProvider.GetRequiredService<IMessageAuditLogger>();
            await auditLogger.LogAsync(new MessageAuditLog
            {
                CorrelationId = correlationId.Value,
                ExchangeName = exchange,
                EventType = eventType,
                PointOfInterestId = pointOfInterestId,
                RetryCount = retryCount,
                ErrorMessage = errorMessage,
                CreatedAt = DateTime.UtcNow
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write audit log for {Exchange}, event {EventType}, correlation {CorrelationId}", exchange, eventType, correlationId);
        }
    }

    private async Task TryRequeueAsync(IChannel channel, ulong deliveryTag)
    {
        try
        {
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not requeue delivery {DeliveryTag} while stopping; the broker will redeliver it.", deliveryTag);
        }
    }

    public async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{Service} is stopping.", nameof(MessageSubscriberBase<T>));

        await _stopping.CancelAsync();

        try
        {
            if (_channel is not null) await _channel.CloseAsync(stoppingToken);
            if (_connection is not null) await _connection.CloseAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing RabbitMQ connection for queue '{Queue}'.", _queueName);
        }
        finally
        {
            _channel?.Dispose();
            _connection?.Dispose();
            _stopping.Dispose();
        }
    }
}
