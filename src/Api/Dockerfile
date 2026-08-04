#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
ENV ASPNETCORE_URLS=http://+:80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# The PAT arrives as a BuildKit secret, not a build ARG. As an ARG, BuildKit
# expanded it when echoing this RUN instruction, printing the raw token into the
# build output and any captured CI log. Mounted as a secret it exists only as a
# file inside this one RUN, never in the instruction text, the build log, or the
# image. Callers supply it with --secret id=ghpat,env=GITHUB_PAT_PACKAGE_REGISTRY
# (digihouse's script/image.ps1 builds this image as part of a release).
RUN --mount=type=secret,id=ghpat \
	dotnet nuget add source --username BigRedProf --password "$(cat /run/secrets/ghpat)" --store-password-in-clear-text --name github "https://nuget.pkg.github.com/BigRedProf/index.json"
COPY [".config/dotnet-tools.json", ".config/"]
RUN dotnet tool restore
COPY ["src/Api/BigRedProf.Stories.Api.csproj", "src/Api/"]
COPY ["src/Core/BigRedProf.Stories.Core.csproj", "src/Core/"]
RUN dotnet restore "src/Api/BigRedProf.Stories.Api.csproj"
COPY . .
WORKDIR "/src/src/Api"
RUN dotnet build "BigRedProf.Stories.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BigRedProf.Stories.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigRedProf.Stories.Api.dll"]