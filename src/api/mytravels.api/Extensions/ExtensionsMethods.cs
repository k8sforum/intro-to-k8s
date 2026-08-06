using mytravels.contract;
using mytravels.contract.Dtos;
using mytravels.contract.Responses;

namespace mytravels.api.Extensions
{
    public static class ExtensionsMethods
    {
        public static List<PointOfInterestDto> ToDto(this List<GetPointOfInterestResponse> target)
        {
            IEnumerable<IGrouping<int, GetPointOfInterestResponse>> groupedPointOfInterestResponses = target.GroupBy(x => x.PointOfInterestId);
            List<PointOfInterestDto> dtos = new();
            foreach (var group in groupedPointOfInterestResponses)
            {
                GetPointOfInterestResponse first = group.First();
                PointOfInterestDto dto = new PointOfInterestDto
                {
                    Id = first.PointOfInterestId,
                    DateCreated = first.DateCreated,
                    DateTaken = first.DateTaken,
                    FormattedAddress = first.FormattedAddress,
                    Latitude = first.Latitude,
                    Longitude = first.Longitude,
                    PointOfInterestKey = first.PointOfInterestKey,
                    Tags = group.ToList()
                                .Where(x => x.TagId is not null)
                                .Select(x => new TagDto
                                {
                                    Id = x.TagId ?? 0,
                                    Name = x.TagName
                                })
                                .ToList()
                };
                dtos.Add(dto);
            }
            return dtos;
        }
    }
}
