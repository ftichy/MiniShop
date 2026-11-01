# Cloud krok 1: image služby Ordering + UI (MiniShop.Web).
# Build z kořene repozitáře (projekt potřebuje i Application, Infrastructure, …):
#   docker build -f src/MiniShop.Web/Dockerfile -t minishop-web .
#
# Dvě fáze (multi-stage): "build" má celé SDK a zkompiluje aplikaci, do výsledné image
# se zkopíruje jen výsledek nad malým runtime obrazem. Kompilátor ani zdrojáky v produkci nejsou.

# --platform=$BUILDPLATFORM: SDK běží na architektuře stroje, který staví (rychle), a výstup se jen
# překompiluje pro cílovou architekturu (-a $TARGETARCH) – tak vznikne image pro ARM VM i na x86 PC (krok 7).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Nejdřív jen projektové soubory a restore. Docker si vrstvu zapamatuje (cache) a při změně kódu
# se balíčky NuGet nestahují znovu – restore se zopakuje jen při změně některého .csproj.
COPY Directory.Build.props ./
COPY src/MiniShop.Contracts/MiniShop.Contracts.csproj src/MiniShop.Contracts/
COPY src/MiniShop.Domain/MiniShop.Domain.csproj src/MiniShop.Domain/
COPY src/MiniShop.Application/MiniShop.Application.csproj src/MiniShop.Application/
COPY src/MiniShop.Infrastructure/MiniShop.Infrastructure.csproj src/MiniShop.Infrastructure/
COPY src/MiniShop.ServiceDefaults/MiniShop.ServiceDefaults.csproj src/MiniShop.ServiceDefaults/
COPY src/MiniShop.Web/MiniShop.Web.csproj src/MiniShop.Web/
RUN dotnet restore src/MiniShop.Web/MiniShop.Web.csproj -a $TARGETARCH

COPY src/ src/
# Příručka (/Docs) – publish ji zkopíruje vedle DLL (viz Content v MiniShop.Web.csproj).
COPY docs/prirucka/ docs/prirucka/
RUN dotnet publish src/MiniShop.Web/MiniShop.Web.csproj -c Release -a $TARGETARCH --no-restore \
    -p:UseAppHost=false -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# /data = místo pro trvalá data (SQLite, klíče, logy); v kroku 2 se na něj připojí volume.
# Aplikace neběží jako root, ale jako uživatel "app" ($APP_UID z base image) – proto mu složku předáme.
RUN mkdir -p /data && chown $APP_UID /data
USER $APP_UID

# Výchozí nastavení pro kontejner (12-factor: jde přepsat proměnnou prostředí při spuštění).
# "__" v názvu = ":" v konfiguraci, WriteTo__1 = druhý cíl Serilogu (soubor) v appsettings.json.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Ordering="Data Source=/data/ordering.db" \
    ConnectionStrings__Journeys="Data Source=/data/journeys.db" \
    ConnectionStrings__Accounts="Data Source=/data/users.db" \
    DataProtection__KeysPath=/data/keys \
    Serilog__WriteTo__1__Args__path=/data/logs/minishop-.log
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MiniShop.Web.dll"]
