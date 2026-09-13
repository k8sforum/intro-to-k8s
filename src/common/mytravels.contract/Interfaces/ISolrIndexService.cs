using mytravels.contract.Responses;

namespace mytravels.contract.Interfaces;

public interface ISolrIndexService
{
    /// <summary>
    /// Indexes the point identified by <paramref name="pointOfInterestId"/>. The row is resolved to its
    /// PointOfInterestKey first and the newest row for that key is what actually gets indexed, which makes
    /// indexing idempotent and independent of the order messages arrive in.
    /// </summary>
    Task IndexAsync(int pointOfInterestId, CancellationToken cancellationToken);

    /// <summary>
    /// Indexes a batch of read-model rows. Rows are grouped by PointOfInterestKey, so the caller may pass
    /// the row-per-tag output of spGetPointOfInterest() straight through.
    /// </summary>
    Task IndexBatchAsync(IEnumerable<GetPointOfInterestResponse> rows, CancellationToken cancellationToken);

    /// <summary>Deletes every document in the collection.</summary>
    Task PurgeAsync(CancellationToken cancellationToken);

    /// <summary>Idempotently creates the fields the app queries. Safe to call on every startup.</summary>
    Task EnsureSchemaAsync(CancellationToken cancellationToken);
}
