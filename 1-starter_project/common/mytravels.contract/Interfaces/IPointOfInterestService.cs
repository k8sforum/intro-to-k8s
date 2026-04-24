using Microsoft.AspNetCore.Http;
using mytravels.contract.Responses;

namespace mytravels.contract.Interfaces;

public interface IPointOfInterestService
{
    Task<List<GetPointOfInterestResponse>> GetAsync(CancellationToken cancellationToken);
    Task<List<GetPointOfInterestResponse>> GetAsync(string tagName, CancellationToken cancellationToken);
    Task<int> SaveFileAsPointOfInsterestAsync(IFormFile file, CancellationToken cancellationToken);
    Task<int> UpdateStatusAsync(string pointOfInterestKey, int pointOfInterestStatusId, CancellationToken cancellationToken);
    Task<int> UpdatePointOfInterestAsync(IFormFile file, string pointOfInterestKey, CancellationToken cancellationToken);
}
