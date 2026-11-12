namespace mytravels.contract.Dtos;

public class SolrSearchQuery
{
    /// <summary>Free-text term matched against the formatted address, tags and description.</summary>
    public string Term { get; set; }

    public int Rows { get; set; } = 100;

    public int Start { get; set; }

    /// <summary>
    /// Set by the caller to ask for the term to be rewritten by <c>ISearchTermRewriter</c> before it reaches
    /// SOLR. The api sets it from the <c>enable-nlp-search</c> flag; nothing else sets it today.
    /// </summary>
    public bool RewriteTerm { get; set; }

    /// <summary>
    /// The rewritten term, filled in by <c>PointOfInterestService.SearchAsync</c>. When present, SolrSearchService
    /// searches the location and keywords field by field instead of matching <see cref="Term"/> across every field.
    /// </summary>
    public SearchTermRewrite Rewrite { get; set; }
}
