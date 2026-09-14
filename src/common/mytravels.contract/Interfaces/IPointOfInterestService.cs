using Microsoft.AspNetCore.Http;
using mytravels.contract.Dtos;
using mytravels.contract.Responses;

namespace mytravels.contract.Interfaces;

public interface IPointOfInterestService
{
    Task<List<GetPointOfInterestResponse>> GetAsync(CancellationToken cancellationToken);
    Task<List<GetPointOfInterestResponse>> SearchAsync(SolrSearchQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Queues a full SOLR rebuild and returns the correlation id it was published under, so the caller can
    /// follow it through the traceability UI. The rebuild itself happens in the messaging worker.
    /// </summary>
    Task<Guid> RequestReindexAsync(bool purgeFirst, CancellationToken cancellationToken);
    Task<int> SaveFileAsPointOfInsterestAsync(IFormFile file, CancellationToken cancellationToken);
    Task<int> SaveFileAsPointOfInsterestAsync(IFormFile file, SaveCoordinatesDto coordinates, CancellationToken cancellationToken);
    Task<int> UpdatePointOfInterestAsync(IFormFile file, string pointOfInterestKey, CancellationToken cancellationToken);
    Task<string> GetImageAsync(int id, CancellationToken cancellationToken);
}