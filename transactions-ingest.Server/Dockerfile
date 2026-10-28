# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration).
# Fast mode mounts the project folder at /app, so it shares transactions.db and data/sources with `dotnet run`.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# SDK plus Node for the Angular build. Angular 22 requires Node ^22.22.3, ^24.15.0 or >=26 (engines in @angular/core).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS with-node
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

# This stage is used to build the service project
FROM with-node AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Project files first so the restore layer is cached; every project the Server references must be here.
COPY ["transactions-ingest.Server/transactions-ingest.Server.csproj", "transactions-ingest.Server/"]
COPY ["transactions-ingest.Core/transactions-ingest.Core.csproj", "transactions-ingest.Core/"]
COPY ["transactions-ingest.client/transactions-ingest.client.esproj", "transactions-ingest.client/package.json", "transactions-ingest.client/package-lock.json", "transactions-ingest.client/"]
RUN dotnet restore "./transactions-ingest.Server/transactions-ingest.Server.csproj"
COPY . .
WORKDIR "/src/transactions-ingest.Server"
RUN dotnet build "./transactions-ingest.Server.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./transactions-ingest.Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# The app runs as a non-root user. Keep the database and the mock source files in /data, apart from the app files
# and mountable as a volume, and generate the mock files on first start, since Production doesn't by default (D14).
USER root
RUN mkdir -p /data && chown $APP_UID /data
USER $APP_UID
ENV ConnectionStrings__Transactions="Data Source=/data/transactions.db" \
    Import__MockSources__Directory=/data/sources \
    Import__MockSources__GenerateMissing=true

ENTRYPOINT ["dotnet", "transactions-ingest.Server.dll"]
