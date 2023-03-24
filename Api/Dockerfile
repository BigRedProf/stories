#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
ARG GITHUB_PAT_PACKAGE_REGISTRY
WORKDIR /src
COPY ["Api/BigRedProf.Stories.Api.csproj", "Api/"]
COPY ["Stories/BigRedProf.Stories.csproj", "Stories/"]
RUN dotnet nuget add source --username BigRedProf --password ${GITHUB_PAT_PACKAGE_REGISTRY} --store-password-in-clear-text --name github "https://nuget.pkg.github.com/BigRedProf/index.json"
RUN dotnet restore "Api/BigRedProf.Stories.Api.csproj"
COPY . .
WORKDIR "/src/Api"
RUN dotnet build "BigRedProf.Stories.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BigRedProf.Stories.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigRedProf.Stories.Api.dll"]