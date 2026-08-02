# Merges the arm64 (macOS) and amd64 (Windows) images pushed by
# docker-compose.build.yml into a single multi-arch tag per service.
#
# Run once, from either machine, after both architectures have been built
# and pushed:
#   ./merge-manifests.ps1
#
# Keep this list in sync with the image names/versions in ../docker-compose.build.yml
$images = @(
    "tshepontlhokoa/mytravels-migrations:v1.0.1"
    "tshepontlhokoa/mytravels-api:v1.0.5"
    "tshepontlhokoa/mytravels-messaging:v1.0.6"
    "tshepontlhokoa/mytravels-web:v1.0.1"
)

foreach ($image in $images) {
    Write-Host "Merging $image-arm64 + $image-amd64 -> $image"
    docker buildx imagetools create -t $image "$image-arm64" "$image-amd64"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to merge manifest for $image"
    }
}

Write-Host "Done. Verify with: docker buildx imagetools inspect <image>:<tag>"
