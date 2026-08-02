#!/usr/bin/env bash
# Merges the arm64 (macOS) and amd64 (Windows/Ubuntu) images pushed by
# docker-compose.build.yml into a single multi-arch tag per service.
#
# Run once, from either machine, after both architectures have been built
# and pushed:
#   ./merge-manifests.sh
#
# Keep this list in sync with the image names/versions in ../docker-compose.build.yml
set -euo pipefail

images=(
    "tshepontlhokoa/mytravels-migrations:v1.0.1"
    "tshepontlhokoa/mytravels-api:v1.0.5"
    "tshepontlhokoa/mytravels-messaging:v1.0.7"
    "tshepontlhokoa/mytravels-web:v1.0.2"
)

for image in "${images[@]}"; do
    echo "Merging ${image}-arm64 + ${image}-amd64 -> ${image}"
    docker buildx imagetools create -t "$image" "${image}-arm64" "${image}-amd64"
done

echo "Done. Verify with: docker buildx imagetools inspect <image>:<tag>"
