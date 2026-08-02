using mytravels.contract.Interfaces;

namespace mytravels.contract.Messages
{
    public class PointOfInterestMessage : IMessage
    {
        public Guid CorrelationId { get; set; }
        public int PointOfInterestId { get; set; }
    }
}
