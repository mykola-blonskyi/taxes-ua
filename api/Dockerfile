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
RUN apt-get update && apt-get install -y --no-install-recommends curl tzdata && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s CMD curl -fsS http://localhost:8080/api/health || exit 1
USER $APP_UID
ENTRYPOINT ["dotnet", "TaxesUa.Api.dll"]
