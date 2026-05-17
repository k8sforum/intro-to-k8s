namespace mytravels.contract.Interfaces;

public interface IMessage
{
    Guid CorrelationId { get; set; }
}