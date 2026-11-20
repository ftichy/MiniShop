# E-shop krok 26A: image simulovaného dopravce (MiniShop.CarrierSimulator).
# Build z kořene repozitáře:
#   docker build -f src/MiniShop.CarrierSimulator/Dockerfile -t minishop-carrier-sim .
# Simulátor nemá žádnou referenci na projekty MiniShopu („cizí firma“) – kopíruje se jen jeho složka.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY Directory.Build.props ./
COPY src/MiniShop.CarrierSimulator/MiniShop.CarrierSimulator.csproj src/MiniShop.CarrierSimulator/
RUN dotnet restore src/MiniShop.CarrierSimulator/MiniShop.CarrierSimulator.csproj -a $TARGETARCH

COPY src/MiniShop.CarrierSimulator/ src/MiniShop.CarrierSimulator/
RUN dotnet publish src/MiniShop.CarrierSimulator/MiniShop.CarrierSimulator.csproj -c Release -a $TARGETARCH --no-restore \
    -p:UseAppHost=false -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN mkdir -p /data && chown $APP_UID /data
USER $APP_UID

# Zásilky a čekající webhooky v /data/carrier-state.json (volume carrier-data).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Carrier__DataPath=/data
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MiniShop.CarrierSimulator.dll"]
