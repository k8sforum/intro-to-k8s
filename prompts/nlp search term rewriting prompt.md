Add a natural-language query rewriting step to POI search.

Context
- GET /api/pointofinterest/search (PointOfInterestController.SearchAsync) builds a
  SolrSearchQuery { Term, Rows, Start } and calls IPointOfInterestService.SearchAsync,
  which queries SOLR via SolrSearchService (qf=formatted_address^5 tags^3 description^1).
- Indexed content: formatted_address (geocoded address), tags (Claude-generated,
  tokenised), description (Claude-generated prose). See SolrIndexService.Fields.
- The MCP tool search_pointofinterest calls the same service, so where the rewrite
  lives decides whether mcp gets it too.

Goal
Users type things like "beach photos from Cape Town" or "that waterfall hike".
Before querying SOLR, run the raw term through an LLM (Anthropic, reusing the
existing AnthropicApiKey / AnthropicModel config pattern) that turns it into
terms matching what is actually indexed: drop filler words ("photos", "that",
"from"), map concepts to likely tag vocabulary, and separate place names
(target formatted_address) from subject terms (target tags/description).

Requirements
1. New interface ISearchTermRewriter in contract, implementation in common,
   called from PointOfInterestService.SearchAsync (so both api and mcp benefit).
2. Output is structured (JSON via tool use / structured output), e.g.
   { location: string?, keywords: string[] }, not free text. SolrSearchService
   builds the SOLR query from it, e.g. location boosted on formatted_address,
   keywords OR'd across tags and description.
3. Skip the LLM for short/simple terms (e.g. 1-2 words) and pass them straight through.
4. Fall back to the raw term on any LLM failure or timeout (~2s); search must never
   fail because the rewriter did.
5. Gate it behind a new Flagsmith flag `enable-nlp-search` (default false),
   evaluated in api. Seed it in every flagsmith-seed step/Job like the other three.
6. Cache rewrites in-memory by normalised term (short TTL) since the web search box
   fires on every debounced keystroke.
7. Log original term, rewritten query and latency; add an OTel span around the call.
8. Optionally improve the prompt with real vocabulary: give the model the top N
   tags from SOLR (facet on tags) so it maps to tags that actually exist.

Wiring (per CLAUDE.md)
- AnthropicApiKey/AnthropicModel currently live only on messaging; add them to
  api (and mcp if in scope) across all stages: Compose 1-2, manifests + Secrets
  3/4/5/6, runbooks.
- Bump api (and mcp/common consumers) image tags in lockstep everywhere listed.

Before coding, propose the prompt you'll send the model and the SOLR query shape,
and list the files you'll touch.
