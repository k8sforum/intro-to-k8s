using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using mytravels.contract.Interfaces;

namespace mytravels.common.Services;

public class MessagePublisher : IMessagePublisher
{
    private readonly IConnectionFactory _factory;
    private readonly ILogger<MessagePublisher> _logger;
    private static readonly ActivitySource _activitySource = new("MyTravels.RabbitMQ");

    public MessagePublisher(IConnectionFactory factory, ILogger<MessagePublisher> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync<T>(string exchange, T message, CancellationToken cancellationToken)
    {
        using (var activity = _activitySource.StartActivity($"{exchange} publish", ActivityKind.Producer))
        {
            IConnection connection = await _factory.CreateConnectionAsync(cancellationToken);
            IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

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

            // AMQP correlation: use message's CorrelationId
            var correlationIdProperty = message.GetType().GetProperty("CorrelationId");
            if (correlationIdProperty != null)
            {
                var messageCorrelationId = correlationIdProperty.GetValue(message) as Guid?;
                if (messageCorrelationId.HasValue && messageCorrelationId != Guid.Empty)
                {
                    properties.CorrelationId = messageCorrelationId.ToString();
                }
            }

            string str = JsonConvert.SerializeObject(message);
            byte[] body = Encoding.UTF8.GetBytes(str);

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
