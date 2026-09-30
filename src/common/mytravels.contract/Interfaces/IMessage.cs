namespace mytravels.contract.Interfaces;

public interface IMessage
{
    Guid CorrelationId { get; set; }

    /// <summary>
    /// The point of interest this message concerns, for the audit trail. Null for messages that span every point
    /// (such as a reindex request), which is why it is a default member rather than a required one.
    /// </summary>
    int? AuditPointOfInterestId => null;
}
