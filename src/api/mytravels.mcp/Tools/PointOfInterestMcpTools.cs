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
    [Description("Searches saved points of interest across their formatted address, tags and AI-generated description, ranked by relevance.")]
    public async Task<List<PointOfInterestDto>> SearchPointOfInterestAsync(
        [Description("Free-text search term, matched against the formatted address, the tags and the description.")] string term,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            throw new McpException($"Search term is required.");
        }

        SolrSearchQuery query = new()
        {
            Term = term
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
