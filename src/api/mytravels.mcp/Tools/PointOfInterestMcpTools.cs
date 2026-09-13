using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;

namespace mytravels.mcp.Tools;

[McpServerToolType]
public class PointOfInterestMcpTools
{
    private readonly IPointOfInterestService _service;

    public PointOfInterestMcpTools(IPointOfInterestService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    [McpServerTool(Name = "search_pointofinterest")]
    [Description("Searches saved points of interest across their formatted address, tags and AI-generated description, ranked by relevance. Optionally narrow the results to an exact tag and to a capture-date range.")]
    public async Task<List<PointOfInterestDto>> SearchPointOfInterestAsync(
        [Description("Free-text search term, matched against the formatted address, the tags and the description.")] string term,
        [Description("Optional exact tag name to filter by, for example 'beach'.")] string tag,
        [Description("Optional inclusive start of the capture-date range, as an ISO-8601 date such as 2025-06-01.")] DateTime? from,
        [Description("Optional inclusive end of the capture-date range, as an ISO-8601 date such as 2025-08-31.")] DateTime? to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(term) && string.IsNullOrWhiteSpace(tag) && from is null && to is null)
        {
            throw new McpException($"At least one of '{nameof(term)}', '{nameof(tag)}', '{nameof(from)}' or '{nameof(to)}' is required.");
        }

        SolrSearchQuery query = new()
        {
            Term = term,
            Tag = tag,
            From = from,
            To = to
        };

        var response = await _service.SearchAsync(query, cancellationToken);
        return ToDto(response);
    }

    private static List<PointOfInterestDto> ToDto(List<mytravels.contract.Responses.GetPointOfInterestResponse> target)
    {
        IEnumerable<IGrouping<int, mytravels.contract.Responses.GetPointOfInterestResponse>> groupedPointOfInterestResponses = target.GroupBy(x => x.PointOfInterestId);
        List<PointOfInterestDto> dtos = new();
        foreach (var group in groupedPointOfInterestResponses)
        {
            var first = group.First();
            PointOfInterestDto dto = new PointOfInterestDto
            {
                Id = first.PointOfInterestId,
                DateCreated = first.DateCreated,
                DateTaken = first.DateTaken,
                FormattedAddress = first.FormattedAddress,
                Description = first.Description,
                Latitude = first.Latitude,
                Longitude = first.Longitude,
                PointOfInterestKey = first.PointOfInterestKey,
                Tags = group
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
