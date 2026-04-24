using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using mytravels.contract.Interfaces;

namespace mytravels.common.Services
{
    public class GeoService : IGeoService
    {
        public GeoLocation ExtractGeoLocation(Stream stream)
        {
            var directories = ImageMetadataReader.ReadMetadata(stream);
            // Find the GPS directory
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();
            if (gpsDirectory is null)
            {
                return new GeoLocation(0, 0);
            }
            else
            {
                // Get the latitude and longitude
                GeoLocation geoLocation = gpsDirectory.GetGeoLocation();
                if (geoLocation is null)
                {
                    return new GeoLocation(0, 0);
                }
                return geoLocation;
            }
        }
    }
}
