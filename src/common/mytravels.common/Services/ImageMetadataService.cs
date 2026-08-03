using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using mytravels.contract.Interfaces;

namespace mytravels.common.Services
{
    public class ImageMetadataService : IGeoService
    {
        public ImageMetadata ExtractImageMetadata(Stream stream)
        {
            IReadOnlyList<MetadataExtractor.Directory> directories = ImageMetadataReader.ReadMetadata(stream);

            GeoLocation geoLocation = ExtractGeoLocation(directories);
            DateTime? dateTaken = ExtractDateTaken(directories);

            return new ImageMetadata(geoLocation, dateTaken);
        }

        private static GeoLocation ExtractGeoLocation(IReadOnlyList<MetadataExtractor.Directory> directories)
        {
            var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();
            if (gpsDirectory is null) return new GeoLocation(0, 0);

            GeoLocation geoLocation = gpsDirectory.GetGeoLocation();
            return geoLocation ?? new GeoLocation(0, 0);
        }

        private static DateTime? ExtractDateTaken(IReadOnlyList<MetadataExtractor.Directory> directories)
        {
            var exifDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (exifDirectory is null) return null;

            return exifDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime dateTaken)
                ? dateTaken
                : null;
        }
    }
}
