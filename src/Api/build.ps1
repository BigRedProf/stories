dotnet build
dotnet build -c Release

docker build -f "Dockerfile" `
	--force-rm `
	-t bigredprofstoriesapi `
	--build-arg GITHUB_PAT_PACKAGE_REGISTRY `
	.\..