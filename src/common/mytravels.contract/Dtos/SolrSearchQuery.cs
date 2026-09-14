namespace mytravels.contract.Dtos;

public class SolrSearchQuery
{
    /// <summary>Free-text term matched against the formatted address, tags and description.</summary>
    public string Term { get; set; }

    public int Rows { get; set; } = 100;

    public int Start { get; set; }
}
