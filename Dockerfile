# syntax=docker/dockerfile:1

# ---- Build stage ----
# Uses the .NET 9 SDK to restore, build, and publish the API host project.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files first to leverage Docker layer caching for restore.
COPY StreamingPanel.sln ./
COPY src/StreamingPanel.Api/StreamingPanel.Api.csproj src/StreamingPanel.Api/
COPY src/StreamingPanel.Core/StreamingPanel.Core.csproj src/StreamingPanel.Core/
COPY src/StreamingPanel.Infrastructure/StreamingPanel.Infrastructure.csproj src/StreamingPanel.Infrastructure/

# Restore only the API project graph (pulls in Core + Infrastructure transitively).
RUN dotnet restore src/StreamingPanel.Api/StreamingPanel.Api.csproj

# Copy the rest of the sources and publish a release build.
COPY . .
RUN dotnet publish src/StreamingPanel.Api/StreamingPanel.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ---- Runtime stage ----
# Uses the ASP.NET Core 9 runtime image; smaller than the SDK image.
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# The API listens on 8080 inside the container (mapped to a host port by compose).
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "StreamingPanel.Api.dll"]
