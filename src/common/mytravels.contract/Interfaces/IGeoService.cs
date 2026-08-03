using MetadataExtractor;

namespace mytravels.contract.Interfaces;

public interface IGeoService
{
    ImageMetadata ExtractImageMetadata(Stream stream);
}

public record ImageMetadata(GeoLocation GeoLocation, DateTime? DateTaken);
