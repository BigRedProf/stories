$ErrorActionPreference = "Stop"

.\stop-local.ps1
docker run -d --restart unless-stopped `
	--name BigRedProf.Stories.Api `
	--network mike-net `
	-p 43027:80 `
	-e "ASPNETCORE_ENVIRONMENT=Development" `
	bigredprofstoriesapi 
