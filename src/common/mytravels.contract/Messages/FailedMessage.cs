using mytravels.contract.Interfaces;

namespace mytravels.contract.Messages
{
    public class FailedMessage : IMessage
    {
        public Guid CorrelationId { get; set; }
        public int PointOfInterestId { get; set; }
        public string OriginalExchange { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime FailedAt { get; set; }
    }
}
