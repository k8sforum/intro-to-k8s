using mytravels.contract.Entities;
using mytravels.contract.Interfaces;

namespace mytravels.domain.Features.Traceability;

public class MessageAuditLogger : IMessageAuditLogger
{
    private readonly ICoreDbContext _context;

    public MessageAuditLogger(ICoreDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task LogAsync(MessageAuditLog entry, CancellationToken cancellationToken)
    {
        _context.AddObject(entry);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
