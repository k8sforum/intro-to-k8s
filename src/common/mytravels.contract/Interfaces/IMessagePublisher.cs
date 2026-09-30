namespace mytravels.contract.Interfaces;

public interface IMessagePublisher
{
    Task PublishAsync<T>(string exchange, T obj, CancellationToken cancellationToken) where T : IMessage;
    Task PublishRawAsync(string exchange, string payloadJson, Guid correlationId, int? pointOfInterestId, CancellationToken cancellationToken);
}