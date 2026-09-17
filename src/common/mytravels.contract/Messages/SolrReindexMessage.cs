using mytravels.contract.Interfaces;

namespace mytravels.contract.Messages
{
    /// <summary>
    /// Requests a full rebuild of the SOLR collection from PostgreSQL. It deliberately carries no
    /// PointOfInterestId - the rebuild spans every point - which the audit logging in
    /// MessageSubscriberBase tolerates because it reads that property reflectively.
    /// </summary>
    public class SolrReindexMessage : IMessage
    {
        public Guid CorrelationId { get; set; }
        public bool PurgeFirst { get; set; } = true;
    }
}
