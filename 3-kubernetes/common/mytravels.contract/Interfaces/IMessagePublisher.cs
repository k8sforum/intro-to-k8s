namespace mytravels.contract.Interfaces;

public interface IMessagePublisher
{
    Task PublishAsync<T>(string exchange, T obj, CancellationToken cancellationToken);
}