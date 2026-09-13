namespace mytravels.contract.Dtos;

public class SolrSearchQuery
{
    /// <summary>Free-text term matched against the formatted address, tags and description.</summary>
    public string Term { get; set; }

    /// <summary>Exact tag name to filter on. Serves the path the spGetPointOfInterestByTagName filter used to.</summary>
    public string Tag { get; set; }

    /// <summary>Inclusive lower bound on the capture date, falling back to the creation date when the point has none.</summary>
    public DateTime? From { get; set; }

    /// <summary>Inclusive upper bound on the capture date, falling back to the creation date when the point has none.</summary>
    public DateTime? To { get; set; }

    public int Rows { get; set; } = 100;

    public int Start { get; set; }
}
