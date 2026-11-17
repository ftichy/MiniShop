# Mikroservisy M2C: image služby Notifications (e-maily, oznámení push) (MiniShop.Notifications.Api).
# Build z kořene repozitáře:
#   docker build -f src/MiniShop.Notifications.Api/Dockerfile -t minishop-notifications-api .
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
COPY src/MiniShop.Notifications.Api/MiniShop.Notifications.Api.csproj src/MiniShop.Notifications.Api/
RUN dotnet restore src/MiniShop.Notifications.Api/MiniShop.Notifications.Api.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/MiniShop.Notifications.Api/MiniShop.Notifications.Api.csproj -c Release -a $TARGETARCH --no-restore \
    -p:UseAppHost=false -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN mkdir -p /data && chown $APP_UID /data
USER $APP_UID

# Bez formulářů (Data Protection klíče netřeba) a bez souborů – data jsou jen v PostgreSQL (schéma notifications).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Serilog__WriteTo__1__Args__path=/data/logs/notifications-{Instance}-.log
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MiniShop.Notifications.Api.dll"]
