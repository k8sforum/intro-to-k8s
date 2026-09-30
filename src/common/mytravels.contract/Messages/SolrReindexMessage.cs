using mytravels.contract.Interfaces;

namespace mytravels.contract.Messages
{
    /// <summary>
    /// Requests a full rebuild of the SOLR collection from PostgreSQL. It deliberately carries no
    /// PointOfInterestId - the rebuild spans every point - so <see cref="IMessage.AuditPointOfInterestId"/>
    /// keeps its null default.
    /// </summary>
    public class SolrReindexMessage : IMessage
    {
        public Guid CorrelationId { get; set; }
        public bool PurgeFirst { get; set; } = true;
    }
}
