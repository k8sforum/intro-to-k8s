using mytravels.contract.Dtos;

namespace mytravels.contract.Interfaces;

public interface ISearchTermRewriter
{
    /// <summary>
    /// Rewrites a natural-language search term into a location and keywords that match what is indexed.
    /// Returns null whenever the raw term should be searched as-is: short terms, an unconfigured rewriter,
    /// a timeout or any other failure. It never throws for a rewriter problem, so search cannot fail because
    /// of it; only a cancellation requested by the caller propagates.
    /// </summary>
    Task<SearchTermRewrite> RewriteAsync(string term, CancellationToken cancellationToken);
}
