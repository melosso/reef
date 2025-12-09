# syntax=docker/dockerfile:1.4

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files first to leverage layer caching
COPY Source/Reef.sln ./
COPY Source/Reef/*.csproj ./Reef/

# Restore dependencies (use BuildKit cache for NuGet packages)
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore ./Reef.sln

# Copy the remaining source files
COPY Source/ .

# Publish the Reef project
WORKDIR /src/Reef
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish Reef.csproj -c Release -o /app/publish /p:UseAppHost=false

# Copy views folder explicitly (not included in publish by default)
COPY Source/Reef/views /app/publish/views

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS runtime
WORKDIR /app

# Install sqlite3 and curl for runtime healthchecks and basic tools
RUN apt-get update && apt-get install -y --no-install-recommends \
    sqlite3 \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Create directories used by the application
RUN mkdir -p /app/exports /app/log /app/.core

# Set environment variables
ENV ASPNETCORE_HTTP_PORTS=8085
ENV ASPNETCORE_ENVIRONMENT=Production
ENV REEF_ENCRYPTION_KEY=""

# Copy published output
COPY --from=build /app/publish .

# Ensure permissions
RUN chmod -R 755 /app

# Healthcheck
HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8085/health || exit 1

EXPOSE 8085

ENTRYPOINT ["dotnet", "Reef.dll"]
