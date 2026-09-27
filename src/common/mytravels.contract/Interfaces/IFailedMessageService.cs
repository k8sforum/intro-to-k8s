using mytravels.contract.Dtos;

namespace mytravels.contract.Interfaces;

public interface IFailedMessageService
{
    Task<List<FailedMessageDto>> GetFailedMessagesAsync(CancellationToken cancellationToken);
    Task<bool> RetryAsync(int id, CancellationToken cancellationToken);
}
