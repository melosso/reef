# syntax=docker/dockerfile:1.4

FROM node:24-alpine AS web
WORKDIR /src/Reef.Web
COPY Source/Reef.Web/package.json Source/Reef.Web/package-lock.json ./
RUN --mount=type=cache,id=npm,target=/root/.npm npm ci --no-audit --no-fund
COPY Source/Reef.Web/ ./
RUN npx vite build --outDir /web/app --emptyOutDir

FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.2 AS build
WORKDIR /src

# Copy solution and project files first to leverage layer caching
COPY Source/Reef.slnx ./
COPY Source/Directory.Packages.props ./
COPY Source/Reef.Tests/*.csproj ./Reef.Tests/
COPY Source/Reef/*.csproj ./Reef/

# Restore dependencies (use BuildKit cache for NuGet packages)
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore ./Reef.slnx

# Copy the remaining source files
COPY Source/ .

# Publish the Reef project
WORKDIR /src/Reef

# Clone .git into Reef directory for MinVer
# (MinVer requires .git to determine version from tags)
RUN git init && \
    git remote add origin $(git -C /src remote get-url origin 2>/dev/null || echo "https://github.com/melosso/reef.git") && \
    { { git fetch --tags origin main && git reset --soft FETCH_HEAD; } || echo "Tags unavailable, MinVer falls back to its default version"; }

COPY --from=web /web/app /src/Reef/wwwroot/app

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish Reef.csproj -c Release -o /app/publish /p:UseAppHost=false /p:SkipWebBuild=true

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.2 AS runtime
WORKDIR /app

# Install sqlite3 and curl for runtime healthchecks and basic tools
RUN apt-get update && apt-get install -y --no-install-recommends \
    sqlite3 \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Create directories used by the application
RUN mkdir -p /app/exports /app/log /app/.core /app/data

# Set environment variables
ENV ASPNETCORE_HTTP_PORTS=
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
