using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Text;
using mytravels.contract.Interfaces;
namespace mytravels.common.Services;

public abstract class MessageSubscriberBase<T> : IHostedService where T : IMessage
{
    private static readonly ActivitySource _activitySource = new("MyTravels.RabbitMQ");

    protected readonly ILogger<MessageSubscriberBase<T>> _logger;
    protected readonly IConfiguration _configuration;
    private readonly string _exchangeName;
    private readonly string _queueName;
    private readonly string _failedExchangeName;
    protected readonly ConnectionFactory _factory;

    protected MessageSubscriberBase(ILogger<MessageSubscriberBase<T>> logger, IConfiguration configuration, string exchangeName, string queueName, string failedExchangeName)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _exchangeName = exchangeName ?? throw new ArgumentNullException(nameof(exchangeName));
        _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
        _failedExchangeName = failedExchangeName ?? throw new ArgumentNullException(nameof(failedExchangeName));

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

        IConnection connection = await _factory.CreateConnectionAsync(cancellationToken);
        _logger.LogInformation("Connected to RabbitMQ at {Uri}", _factory.Uri);

        IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        _logger.LogInformation("Channel created.");

        await channel.ExchangeDeclareAsync(exchange: _exchangeName, type: ExchangeType.Fanout);
        _logger.LogInformation("Exchange '{Exchange}' declared.", _exchangeName);

        await channel.ExchangeDeclareAsync(exchange: _failedExchangeName, type: ExchangeType.Fanout, durable: false, autoDelete: true);
        _logger.LogInformation("Failed exchange '{FailedExchange}' declared.", _failedExchangeName);

        await channel.QueueDeclareAsync(queue: _queueName, durable: true, exclusive: false, autoDelete: false);

        await channel.QueueBindAsync(queue: _queueName, exchange: _exchangeName, routingKey: string.Empty);
        _logger.LogInformation("Queue '{Queue}' bound to exchange '{Exchange}'.", _queueName, _exchangeName);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (model, ea) => await ReceivedAsync(model, ea, channel, cancellationToken);

        await channel.BasicConsumeAsync(_queueName, autoAck: false, consumer: consumer);
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
            }
            catch (Exception ex)
            {
                var correlationId = message != null ? typeof(T).GetProperty("CorrelationId")?.GetValue(message) : null;
                var pointOfInterestId = message != null ? typeof(T).GetProperty("PointOfInterestId")?.GetValue(message) : null;

                _logger.LogError(ex, "Error processing message from {Exchange}, retry count {RetryCount}, PointOfInterestId {PointOfInterestId}, CorrelationId {CorrelationId}",
                    ea.Exchange, retryCount, pointOfInterestId, correlationId);

                // Check retry threshold
                if (retryCount < 3)
                {
                    // Increment retry count header and requeue
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken);
                    _logger.LogWarning("Message nacked and requeued (attempt {RetryCount}/3) for {Exchange}", retryCount + 1, ea.Exchange);
                }
                else
                {
                    // Failed after 3 attempts: publish to -failed exchange
                    var failedMsg = new
                    {
                        CorrelationId = correlationId,
                        PointOfInterestId = pointOfInterestId,
                        OriginalExchange = ea.Exchange,
                        ErrorMessage = ex.Message,
                        FailedAt = DateTime.UtcNow
                    };

                    var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(failedMsg));
                    var properties = new BasicProperties();

                    await channel.BasicPublishAsync(
                        exchange: _failedExchangeName,
                        routingKey: "",
                        mandatory: false,
                        basicProperties: properties,
                        body: body,
                        cancellationToken: cancellationToken
                    );

                    // Acknowledge original message so it stops retrying
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken);
                    _logger.LogError("Message dead-lettered to {FailedExchange} after 3 attempts, CorrelationId {CorrelationId}",
                        _failedExchangeName, failedMsg.CorrelationId);
                }
            }
        }
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{Service} is stopping.", nameof(MessageSubscriberBase<T>));
        return Task.CompletedTask;
    }
}

