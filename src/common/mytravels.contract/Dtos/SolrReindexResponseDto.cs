namespace mytravels.contract.Dtos;

public class SolrReindexResponseDto
{
    /// <summary>Correlation id the reindex was published under, for following it through /api/traceability.</summary>
    public Guid CorrelationId { get; set; }
}
