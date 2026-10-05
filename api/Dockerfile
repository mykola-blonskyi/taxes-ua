FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props TaxesUa.slnx ./
COPY src/TaxesUa.Engine/TaxesUa.Engine.csproj src/TaxesUa.Engine/
COPY src/TaxesUa.Api/TaxesUa.Api.csproj src/TaxesUa.Api/
RUN dotnet restore src/TaxesUa.Api/TaxesUa.Api.csproj
COPY src/ src/
RUN dotnet publish src/TaxesUa.Api/TaxesUa.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 TZ=Europe/Kyiv
# pg_dump takes the pre-migration dump (ADR-027). It must be at least the server's major version
# (the shared instance runs 18), and Debian ships an older one, so it comes from the PostgreSQL repo.
# age encrypts that dump to the backup recovery key on its way to the volume (ADR-031).
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates tzdata age \
    && install -d /usr/share/postgresql-common/pgdg \
    && curl -fsSL -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc https://www.postgresql.org/media/keys/ACCC4CF8.asc \
    && . /etc/os-release \
    && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update && apt-get install -y --no-install-recommends postgresql-client-18 \
    && rm -rf /var/lib/apt/lists/*
# A fresh named volume copies the ownership of the directory it is mounted over, so the non-root
# app user can write the data-protection key ring and the dumps into them.
RUN install -d -m 700 -o "$APP_UID" /var/lib/taxes-ua/keys /var/lib/taxes-ua/dumps
COPY --from=build /app .
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=300s CMD curl -fsS http://localhost:8080/api/health || exit 1
USER $APP_UID
ENTRYPOINT ["dotnet", "TaxesUa.Api.dll"]
