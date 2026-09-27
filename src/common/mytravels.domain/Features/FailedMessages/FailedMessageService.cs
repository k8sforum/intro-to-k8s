using mytravels.contract.Dtos;
using mytravels.contract.Entities;
using mytravels.contract.Interfaces;

namespace mytravels.domain.Features.FailedMessages;

public class FailedMessageService : IFailedMessageService
{
    private readonly ICoreDbContext _context;
    private readonly IMessagePublisher _publisher;

    public FailedMessageService(ICoreDbContext context, IMessagePublisher publisher)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    public async Task<List<FailedMessageDto>> GetFailedMessagesAsync(CancellationToken cancellationToken)
        => await _context.GetFailedMessagesAsync(cancellationToken);

    public async Task<bool> RetryAsync(int id, CancellationToken cancellationToken)
    {
        FailedMessage message = await _context.GetFailedMessageByIdAsync(id, cancellationToken);
        if (message is null || message.ResolvedAt.HasValue) return false;

        await _publisher.PublishRawAsync(message.OriginalExchange, message.Payload, message.CorrelationId, message.PointOfInterestId, cancellationToken);
        await _context.MarkFailedMessageResolvedAsync(id, cancellationToken);
        return true;
    }
}
