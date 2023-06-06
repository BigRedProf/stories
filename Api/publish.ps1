param (
    [Parameter(Position = 0, Mandatory = $true)]
    [string]$Tag
)

$ErrorActionPreference = "Stop"

docker tag bigredprofstoriesapi ghcr.io/bigredprof/bigredprofstoriesapi:$($Tag)
docker push ghcr.io/bigredprof/bigredprofstoriesapi:$($Tag)