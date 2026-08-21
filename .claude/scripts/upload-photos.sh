#!/usr/bin/env bash
#
# Batch-upload every photo in a directory to the MyTravels API as points of interest.
#
# The image bytes go disk -> curl -> API; they never pass through an LLM context
# window, which is why this uses the API's multipart endpoint directly rather than
# the base64-based `upload_photo` MCP tool.
#
# Usage: upload-photos.sh <directory> [api-base-url]
#   api-base-url defaults to http://localhost:5101 (stages 0-2).
#   For stages 3-4 pass http://api.mytravels.local:8080
#
# Output: one TAB-separated record per file -- name<TAB>OK<TAB>id
#                                          or name<TAB>FAIL<TAB>reason
# followed by a TOTAL line. Nothing else is printed, so the summary stays small.

set -uo pipefail

DIR="${1:-}"
API_BASE="${2:-http://localhost:5101}"
ENDPOINT="${API_BASE%/}/api/pointofinterest/image"

if [[ -z "$DIR" ]]; then
  echo "usage: $(basename "$0") <directory> [api-base-url]" >&2
  exit 2
fi

if [[ ! -d "$DIR" ]]; then
  echo "not a directory: $DIR" >&2
  exit 2
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required but not installed" >&2
  exit 2
fi

body=$(mktemp)
trap 'rm -f "$body" "${body}.err"' EXIT

passed=0
failed=0

while IFS= read -r file; do
  name=$(basename "$file")

  # --max-time 300: these are 6-16 MB originals, uploaded whole.
  code=$(curl -sS -o "$body" -w '%{http_code}' --max-time 300 \
              -F "image=@${file}" "$ENDPOINT" 2>"${body}.err")

  if [[ -z "$code" || "$code" == "000" ]]; then
    reason=$(tr '\n' ' ' <"${body}.err" | cut -c1-200)
    printf '%s\t%s\t%s\n' "$name" "FAIL" "no response from $ENDPOINT: ${reason:-connection failed}"
    failed=$((failed + 1))
    continue
  fi

  if [[ "$code" == "200" ]]; then
    id=$(jq -r '.id // .Id // empty' <"$body" 2>/dev/null)
    if [[ -n "$id" ]]; then
      printf '%s\t%s\t%s\n' "$name" "OK" "$id"
      passed=$((passed + 1))
      continue
    fi
  fi

  # Error shape from ApiExceptionMiddleware is {"Message": "...", ...};
  # fall back to the raw body for anything else (ProblemDetails, proxy errors).
  reason=$(jq -r '.Message // .message // .title // empty' <"$body" 2>/dev/null)
  if [[ -z "$reason" ]]; then
    reason=$(tr -d '\r' <"$body" | tr '\n' ' ')
  fi
  printf '%s\t%s\t%s\n' "$name" "FAIL" "HTTP $code: $(printf '%s' "${reason:-empty response}" | cut -c1-200)"
  failed=$((failed + 1))
done < <(find "$DIR" -maxdepth 1 -type f \
              \( -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.png' \
                 -o -iname '*.heic' -o -iname '*.heif' \) | sort)

printf 'TOTAL\t%d passed\t%d failed\n' "$passed" "$failed"
