using mytravels.contract.Dtos;

namespace mytravels.contract.Interfaces;

public interface IImageDescriptionService
{
    Task<ImageDescriptionDto> DescribeAsync(string base64Image, CancellationToken cancellationToken);
}
