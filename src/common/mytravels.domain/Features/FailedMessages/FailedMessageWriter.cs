using mytravels.contract.Entities;
using mytravels.contract.Interfaces;

namespace mytravels.domain.Features.FailedMessages;

public class FailedMessageWriter : IFailedMessageWriter
{
    private readonly ICoreDbContext _context;

    public FailedMessageWriter(ICoreDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task SaveAsync(FailedMessage message, CancellationToken cancellationToken)
    {
        _context.AddObject(message);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
