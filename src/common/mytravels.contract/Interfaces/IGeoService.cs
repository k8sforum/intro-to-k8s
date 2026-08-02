using MetadataExtractor;

namespace mytravels.contract.Interfaces;

public interface IGeoService
{
    GeoLocation ExtractGeoLocation(Stream stream);
}