using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Text;
using mytravels.contract.Interfaces;

namespace mytravels.common.Services;

public class MessagePublisher : IMessagePublisher
{
    private readonly IConnectionFactory _factory;

    public MessagePublisher(IConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task PublishAsync<T>(string exchange, T message, CancellationToken cancellationToken)
    {
        IConnection connection = await _factory.CreateConnectionAsync(cancellationToken);
        IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(exchange: exchange, type: ExchangeType.Fanout);

        string str = JsonConvert.SerializeObject(message);
        byte[] body = Encoding.UTF8.GetBytes(str);

        await channel.BasicPublishAsync
                     (
                        exchange: exchange,
                        routingKey: string.Empty,
                        mandatory: false,
                        body: body,
                        cancellationToken: cancellationToken
                     );

        await channel.CloseAsync(cancellationToken);
        await connection.CloseAsync(cancellationToken);
    }
}
