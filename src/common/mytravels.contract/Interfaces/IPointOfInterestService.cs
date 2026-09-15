using Microsoft.AspNetCore.Http;
using mytravels.contract.Dtos;
using mytravels.contract.Responses;

namespace mytravels.contract.Interfaces;

public interface IPointOfInterestService
{
    /// <summary>
    /// Lists points of interest from SOLR with a single capped <c>*:*</c> query. Only <see cref="SolrSearchQuery.Rows"/>
    /// and <see cref="SolrSearchQuery.Start"/> are read; the term is ignored. A point is absent until the messaging
    /// worker has consumed its <c>index-solr</c> message, and a library larger than <c>Rows</c> is truncated.
    /// </summary>
    Task<List<GetPointOfInterestResponse>> GetAsync(SolrSearchQuery query, CancellationToken cancellationToken);
    Task<List<GetPointOfInterestResponse>> SearchAsync(SolrSearchQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Queues a full SOLR rebuild and returns the correlation id it was published under, so the caller can
    /// follow it through the traceability UI. The rebuild itself happens in the messaging worker.
    /// </summary>
    Task<Guid> RequestReindexAsync(bool purgeFirst, CancellationToken cancellationToken);
    Task<int> SaveFileAsPointOfInsterestAsync(IFormFile file, CancellationToken cancellationToken);
    Task<int> SaveFileAsPointOfInsterestAsync(IFormFile file, SaveCoordinatesDto coordinates, CancellationToken cancellationToken);
    Task<int> UpdatePointOfInterestAsync(IFormFile file, string pointOfInterestKey, CancellationToken cancellationToken);
    Task<string> GetImageAsync(int id, bool resizedImage, CancellationToken cancellationToken);
}