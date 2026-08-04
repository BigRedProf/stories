dotnet build
dotnet build -c Release

# The PAT goes in as a BuildKit secret rather than --build-arg: as a build arg,
# BuildKit expanded it when echoing the Dockerfile's RUN instruction, printing
# the raw token into the build output and any captured log. Requires BuildKit,
# which is the default builder in modern Docker.
docker build -f "Dockerfile" `
	--force-rm `
	-t bigredprofstoriesapi `
	--secret id=ghpat,env=GITHUB_PAT_PACKAGE_REGISTRY `
	.\..\..
