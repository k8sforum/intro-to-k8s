using mytravels.contract.Entities;

namespace mytravels.contract.Interfaces;

public interface IFailedMessageWriter
{
    Task SaveAsync(FailedMessage message, CancellationToken cancellationToken);
}
