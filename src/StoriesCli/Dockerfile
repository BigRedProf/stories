# The stories CLI as an image, for Memorial (BigRedProf/digihouse#397): the backup timer runs
# `backup-all`, the nightly check runs `verify-all`, and a restore runs `restore-all`, each as
# a one-shot container on Memorial's docker network with the tape directory mounted.
#
# Built from the repository root, like the Api image:
#   docker build -t bigredprofstoriescli -f src/StoriesCli/Dockerfile .

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# The same restore inputs as the Api image, for the same reasons: nuget.org only, central
# package versions, and the pack rat compiler the model projects generate with.
COPY ["NuGet.Config", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]
COPY [".config/dotnet-tools.json", ".config/"]
RUN dotnet tool restore
COPY . .
WORKDIR "/src/src/StoriesCli"
# Build, THEN publish -- as the Api image does, and for a reason: the model projects compile
# their generated pack rats through a wildcard over obj/, which MSBuild expands before the pack
# rat compiler has written anything. A lone publish is one pass, compiles no pack rats, and
# produces a CLI that fails at start-up with "The assembly has no [AssemblyRegistrationHelper]
# class". Found by digihouse's Memorial drill.
RUN dotnet build "BigRedProf.Stories.StoriesCli.csproj" -c Release
RUN dotnet publish "BigRedProf.Stories.StoriesCli.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "stories.dll"]
