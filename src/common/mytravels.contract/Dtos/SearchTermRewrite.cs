namespace mytravels.contract.Dtos;

/// <summary>
/// A free-text search term split into what the SOLR index can actually match: a place name for the
/// geocoded <c>formatted_address</c>, and scene words for the AI-generated <c>tags</c> and <c>description</c>.
/// </summary>
public class SearchTermRewrite
{
    /// <summary>The place the photos should come from, or null when the term names none.</summary>
    public string Location { get; set; }

    /// <summary>Lowercase single words describing what should be visible in the photo.</summary>
    public List<string> Keywords { get; set; } = new();
}
