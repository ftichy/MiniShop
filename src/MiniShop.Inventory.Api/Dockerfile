# Cloud krok 1: image služby skladu (MiniShop.Inventory.Api).
# Build z kořene repozitáře:
#   docker build -f src/MiniShop.Inventory.Api/Dockerfile -t minishop-inventory-api .
# Popis jednotlivých částí je v src/MiniShop.Web/Dockerfile – tento je stejný, jen pro jiný projekt.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY Directory.Build.props ./
COPY src/MiniShop.Contracts/MiniShop.Contracts.csproj src/MiniShop.Contracts/
COPY src/MiniShop.Domain/MiniShop.Domain.csproj src/MiniShop.Domain/
COPY src/MiniShop.Application/MiniShop.Application.csproj src/MiniShop.Application/
COPY src/MiniShop.Infrastructure/MiniShop.Infrastructure.csproj src/MiniShop.Infrastructure/
COPY src/MiniShop.ServiceDefaults/MiniShop.ServiceDefaults.csproj src/MiniShop.ServiceDefaults/
COPY src/MiniShop.Inventory.Api/MiniShop.Inventory.Api.csproj src/MiniShop.Inventory.Api/
RUN dotnet restore src/MiniShop.Inventory.Api/MiniShop.Inventory.Api.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/MiniShop.Inventory.Api/MiniShop.Inventory.Api.csproj -c Release -a $TARGETARCH --no-restore \
    -p:UseAppHost=false -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN mkdir -p /data && chown $APP_UID /data
USER $APP_UID

# Sklad nemá formuláře, takže Data Protection klíče nepotřebuje.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Inventory="Data Source=/data/inventory.db" \
    Serilog__WriteTo__1__Args__path=/data/logs/inventory-.log
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MiniShop.Inventory.Api.dll"]
