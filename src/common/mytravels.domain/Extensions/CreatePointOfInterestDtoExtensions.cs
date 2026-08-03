using mytravels.contract.Constants;
using mytravels.contract.Dtos;
using mytravels.contract.Entities;
using mytravels.contract.Lookups;

namespace mytravels.domain.Extensions
{
    public static class CreatePointOfInterestDtoExtensions
    {
        public static PointOfInterest ToEntity(this CreatePointOfInterestDto dto)
        {
            return new PointOfInterest()
            {
                OriginalFileName = dto.OriginalFileName,
                GeneratedBlobName = dto.BlobName,
                Container = BucketNames.NewUploadedImagesContainer,
                DateCreated = DateTime.UtcNow,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                DateTaken = dto.DateTaken,
                PointOfInterestTypeId = dto.PointOfInterestTypeId,
                FormattedAddress = dto.FormattedAddress,
                PointOfInterestKey = Guid.NewGuid().ToString("N"),
                PointOfInterestStatusId = (int)PointOfInterestStatusesEnum.Open,
            };
        }
    }
}
