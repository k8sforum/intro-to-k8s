using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using mytravels.contract.Constants;
using mytravels.contract.Entities;
using mytravels.contract.Interfaces;

namespace mytravels.common.Services;

public class MessagePublisher : IMessagePublisher
{
    private readonly IConnectionFactory _factory;
    private readonly ILogger<MessagePublisher> _logger;
    private readonly IMessageAuditLogger _auditLogger;
    private static readonly ActivitySource _activitySource = new("MyTravels.RabbitMQ");

    public MessagePublisher(IConnectionFactory factory, ILogger<MessagePublisher> logger, IMessageAuditLogger auditLogger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
    }

    public async Task PublishAsync<T>(string exchange, T message, CancellationToken cancellationToken) where T : IMessage
    {
        ArgumentNullException.ThrowIfNull(message);

        string str = JsonConvert.SerializeObject(message);
        byte[] body = Encoding.UTF8.GetBytes(str);

        await PublishBytesAsync(exchange, body, message.CorrelationId, message.AuditPointOfInterestId, cancellationToken);
    }

    public async Task PublishRawAsync(string exchange, string payloadJson, Guid correlationId, int? pointOfInterestId, CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(payloadJson);
        await PublishBytesAsync(exchange, body, correlationId, pointOfInterestId, cancellationToken);
    }

    private async Task PublishBytesAsync(string exchange, byte[] body, Guid? correlationId, int? pointOfInterestId, CancellationToken cancellationToken)
    {
        using (var activity = _activitySource.StartActivity($"{exchange} publish", ActivityKind.Producer))
        {
            await using IConnection connection = await _factory.CreateConnectionAsync(cancellationToken);
            await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            await channel.ExchangeDeclareAsync(exchange: exchange, type: ExchangeType.Fanout, cancellationToken: cancellationToken);

            // Create basic properties with DeliveryMode = persistent
            var properties = new BasicProperties();
            properties.DeliveryMode = DeliveryModes.Persistent;

            // Trace propagation: attach traceparent header
            if (Activity.Current?.Id != null)
            {
                properties.Headers ??= new Dictionary<string, object>();
                properties.Headers["traceparent"] = Activity.Current.Id;
            }

            bool hasCorrelationId = correlationId.HasValue && correlationId != Guid.Empty;
            if (hasCorrelationId)
            {
                properties.CorrelationId = correlationId.ToString();

                try
                {
                    await _auditLogger.LogAsync(new MessageAuditLog
                    {
                        CorrelationId = correlationId.Value,
                        ExchangeName = exchange,
                        EventType = MessageAuditEventTypes.Published,
                        PointOfInterestId = pointOfInterestId,
                        CreatedAt = DateTime.UtcNow
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to write audit log for publish to {Exchange}, correlation {CorrelationId}", exchange, correlationId);
                }
            }

            // Publish message
            await channel.BasicPublishAsync
                         (
                            exchange: exchange,
                            routingKey: string.Empty,
                            mandatory: true, // detect unroutable messages
                            basicProperties: properties,
                            body: body,
                            cancellationToken: cancellationToken
                         );

            await channel.CloseAsync(cancellationToken);
            await connection.CloseAsync(cancellationToken);
        }
    }
}
