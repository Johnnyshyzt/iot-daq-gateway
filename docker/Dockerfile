# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0.203 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json IotDaqGateway.sln ./
COPY src/ ./src/
COPY data/seed/ ./data/seed/
COPY configs/ ./configs/
RUN dotnet publish src/Host/Host.csproj -c Release -o /app/publish --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
# Headless Fake demo. Compose overlays gateway.yaml and sets GATEWAY_CONFIG.
# That file is imported only when the database has no published configuration.
# The field path is Host serving Web and reading the published slot from the database.
# Never COPY Fwlib*.dll / libfwlib32.so — vendor FOCAS binaries stay off image layers.
COPY configs/examples/gateway.docker.yaml /app/gateway.yaml
ENV GATEWAY_CONFIG=/app/gateway.yaml
ENTRYPOINT ["dotnet", "Host.dll"]
