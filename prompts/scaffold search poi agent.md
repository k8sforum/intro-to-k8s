# POI Search Agent - System Prompt & Context

## System Prompt

```
search for point of interests
```

## Agent Purpose

A Claude API agent that searches for points of interest (POIs) using the `search_pointofinterest` MCP tool. The agent accepts a list of search terms as arguments, iterates through each one, and returns a flat list of all matching POIs.

## Agent Behavior

### Input
- **Type**: List of string arguments (search terms)
- **Example**: `["Paris", "Tokyo", "Berlin"]`

### Processing
1. Loop through each search term sequentially
2. For each term:
   - Create a user message: `"Search for points of interest matching: {search_term}"`
   - Call Claude with forced `tool_choice`
   - Tool name: `search_pointofinterest`
   - Tool input: `{"term": search_term}`
3. Use model: `claude-opus-5` (latest)
4. Collect results from all tool invocations

### Output
- **Type**: Flat list of all POI search results
- **Format**: JSON with structure:
  ```json
  {
    "search_terms": ["Paris", "Tokyo", "Berlin"],
    "total_searches": 3,
    "pois": [
      {
        "search_term": "Paris",
        "tool_name": "search_pointofinterest",
        "tool_input": {"term": "Paris"},
        "tool_use_id": "tool_use_..."
      },
      ...
    ]
  }
  ```

## Tool Definition

**Name**: `search_pointofinterest`

**Description**: Searches for points of interest by formatted address, returning all matching records.

**Input Schema**:
```json
{
  "type": "object",
  "properties": {
    "term": {
      "type": "string",
      "description": "Search term to find in the formatted address field."
    }
  },
  "required": ["term"]
}
```

## Design Decisions

- **Sequential Processing**: Search terms are processed one at a time (not in parallel)
- **Forced Tool Choice**: The agent always uses the `search_pointofinterest` tool via forced `tool_choice` rather than letting Claude decide
- **Flat Aggregation**: All POI results are collected into a single flat list rather than grouped by search term
- **MCP Integration**: Uses Anthropic SDK's MCP client integration to connect to the MCP server
