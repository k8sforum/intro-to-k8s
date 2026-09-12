using mytravels.contract.Entities;

namespace mytravels.contract.Interfaces;

public interface IMessageAuditLogger
{
    Task LogAsync(MessageAuditLog entry, CancellationToken cancellationToken);
}
