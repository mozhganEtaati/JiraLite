FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY JiraLite.Api.csproj .
RUN dotnet restore JiraLite.Api.csproj

COPY . .
RUN dotnet publish JiraLite.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The runtime image is Debian-slim and ships neither curl nor wget, so the HEALTHCHECK below has
# nothing to call without this. Lists are removed in the same layer to keep them out of the image.
RUN apt-get update \
    && apt-get install --no-install-recommends -y curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Uploaded files land here (FileStorage:RootPath). It has to exist and be owned by the runtime user
# before the USER switch, otherwise the first avatar/attachment upload fails on a read-only /app.
RUN mkdir -p /app/storage && chown -R $APP_UID:$APP_UID /app/storage

# Production unless a deployment deliberately says otherwise. This is also what keeps
# Database:AutoMigrate inert in this image — see DatabaseMigrator.ShouldMigrateOnStartup.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

# $APP_UID (1654) is the non-root user the .NET base images define. Running as root would let a
# process that escapes the app write anywhere in the container filesystem.
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

# No CMD: arguments are passed at run time. `--migrate` turns this same image into the
# migration step (see docker-compose.prod.yml), so one artifact covers both roles.
ENTRYPOINT ["dotnet", "JiraLite.Api.dll"]
