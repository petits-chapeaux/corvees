FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /source
COPY NuGet.Config Directory.Build.props ./
COPY .config/dotnet-tools.json .config/dotnet-tools.json
COPY src/Corvees.Host/Corvees.Host.csproj src/Corvees.Host/
COPY src/Corvees.Infrastructure/Corvees.Infrastructure.csproj src/Corvees.Infrastructure/
RUN dotnet restore src/Corvees.Host/Corvees.Host.csproj && dotnet tool restore
COPY src/ src/
RUN dotnet publish src/Corvees.Host/Corvees.Host.csproj --configuration Release --no-restore --output /out/app

FROM build AS migration-build
RUN case "$(dpkg --print-architecture)" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) exit 1 ;; \
    esac && \
    ASPNETCORE_ENVIRONMENT=Production AllowedHosts=localhost ConnectionStrings__Corvees='Host=localhost;Database=corvees' \
    dotnet tool run dotnet-ef migrations bundle --project src/Corvees.Infrastructure --startup-project src/Corvees.Host \
      --configuration Release --self-contained --target-runtime "$rid" --output /out/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble AS migrate
ARG SOURCE_REVISION
LABEL org.opencontainers.image.source="https://github.com/petits-chapeaux/corvees" org.opencontainers.image.revision=$SOURCE_REVISION
WORKDIR /app
COPY --from=migration-build /out/efbundle ./efbundle
COPY src/Corvees.Host/appsettings.json ./appsettings.json
ENV ASPNETCORE_ENVIRONMENT=Production DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/efbundle
USER $APP_UID
ENTRYPOINT ["./efbundle"]

FROM postgres:17-bookworm AS tools
ARG SOURCE_REVISION
LABEL org.opencontainers.image.source="https://github.com/petits-chapeaux/corvees" org.opencontainers.image.revision=$SOURCE_REVISION
RUN apt-get update && apt-get install -y --no-install-recommends openssl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --chmod=755 scripts/admin.sh ./admin.sh
COPY docker/app-grants.sql ./app-grants.sql
USER postgres
ENTRYPOINT ["./admin.sh"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS app
ARG SOURCE_REVISION
LABEL org.opencontainers.image.source="https://github.com/petits-chapeaux/corvees" org.opencontainers.image.revision=$SOURCE_REVISION
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/app/ ./
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Corvees.Host.dll"]
