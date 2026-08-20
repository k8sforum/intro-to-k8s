using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;

namespace mytravels.mcp.Tools;

[McpServerToolType]
public class PlaceMcpTools
{
    private const int DefaultLimit = 5;

    private readonly IMapsService _mapsService;

    public PlaceMcpTools(IMapsService mapsService)
    {
        _mapsService = mapsService ?? throw new ArgumentNullException(nameof(mapsService));
    }

    [McpServerTool(Name = "search_place")]
    [Description("Resolves a free-text place name into candidate locations with coordinates, for placing a photo that has no GPS metadata.")]
    public async Task<List<PlaceDto>> SearchPlaceAsync(
        [Description("Free-text place name or address to search for.")] string query,
        [Description("Maximum number of candidates to return. Defaults to 5 if omitted or non-positive.")] int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new McpException($"'{nameof(query)}' is required.");
        return await _mapsService.SearchPlacesAsync(query, limit > 0 ? limit : DefaultLimit, cancellationToken);
    }
}
