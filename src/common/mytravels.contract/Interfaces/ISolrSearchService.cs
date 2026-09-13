using mytravels.contract.Dtos;
using mytravels.contract.Responses;

namespace mytravels.contract.Interfaces;

public interface ISolrSearchService
{
    /// <summary>
    /// Runs a search against SOLR and expands each matching document back into the row-per-tag
    /// shape the PostgreSQL stored procedures produce, so every existing caller is unaffected.
    /// </summary>
    Task<List<GetPointOfInterestResponse>> SearchAsync(SolrSearchQuery query, CancellationToken cancellationToken);
}
