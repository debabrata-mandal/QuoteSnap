FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/QuoteSnap.Contracts/QuoteSnap.Contracts.csproj src/QuoteSnap.Contracts/
COPY src/QuoteSnap.Api/QuoteSnap.Api.csproj src/QuoteSnap.Api/
RUN dotnet restore src/QuoteSnap.Api/QuoteSnap.Api.csproj

COPY src/QuoteSnap.Contracts/ src/QuoteSnap.Contracts/
COPY src/QuoteSnap.Api/ src/QuoteSnap.Api/
RUN dotnet publish src/QuoteSnap.Api/QuoteSnap.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# The named volume copies this directory's ownership on first use, so the non-root user can write to it.
RUN mkdir -p /data && chown "$APP_UID" /data
VOLUME /data

ENV ASPNETCORE_HTTP_PORTS=8080 \
    QuoteSnap__DataDirectory=/data
EXPOSE 8080

COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "QuoteSnap.Api.dll"]
