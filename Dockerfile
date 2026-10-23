# Imagen de producción de Mis·Gastos (ASP.NET Core 10).
# Build:  docker build -t mis-gastos .
# Run:    docker run -p 8080:8080 -e ConnectionStrings__Default="Host=...;Database=...;Username=...;Password=...;SSL Mode=Require" mis-gastos

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ControlGastos.Web/ControlGastos.Web.csproj src/ControlGastos.Web/
RUN dotnet restore src/ControlGastos.Web/ControlGastos.Web.csproj
COPY src/ src/
RUN dotnet publish src/ControlGastos.Web/ControlGastos.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Database__Provider=Postgres \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
# La imagen base trae un usuario sin privilegios: la app no corre como root.
USER $APP_UID
ENTRYPOINT ["dotnet", "ControlGastos.Web.dll"]
