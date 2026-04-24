using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using mytravels.contract.Interfaces;
namespace mytravels.common.Services;

public abstract class MessageSubscriberBase<T> : IHostedService where T : IMessage
{
    protected readonly ILogger<MessageSubscriberBase<T>> _logger;
    protected readonly IConfiguration _configuration;
    private readonly string _exchangeName;
    private readonly string _queueName;
    protected readonly ConnectionFactory _factory;

    protected MessageSubscriberBase(ILogger<MessageSubscriberBase<T>> logger, IConfiguration configuration, string exchangeName, string queueName)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _exchangeName = exchangeName ?? throw new ArgumentNullException(nameof(exchangeName));
        _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));

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

        await channel.QueueDeclareAsync(queue: _queueName, durable: true, exclusive: false, autoDelete: false);

        await channel.QueueBindAsync(queue: _queueName, exchange: _exchangeName, routingKey: string.Empty);
        _logger.LogInformation("Queue '{Queue}' bound to exchange '{Exchange}'.", _queueName, _exchangeName);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (model, ea) =>
        {
            try
            {
                byte[] body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                _logger.LogInformation("Received message: {message}", message);

                if (string.IsNullOrEmpty(message))
                {
                    _logger.LogWarning("Received empty message.");
                    return;
                }

                T? obj = JsonConvert.DeserializeObject<T>(message);
                if (obj is not null)
                {
                    await ProcessMessageAsync(obj, cancellationToken);
                }

                _logger.LogInformation("Processed message successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message.");
            }

            await Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(_queueName, autoAck: true, consumer: consumer);
        _logger.LogInformation("Consuming messages from queue '{Queue}'.", _queueName);
    }

    protected abstract Task ProcessMessageAsync(T obj, CancellationToken cancellationToken);

    public Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{Service} is stopping.", nameof(MessageSubscriberBase<T>));
        return Task.CompletedTask;
    }
}

