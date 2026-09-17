# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0.203 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json IotDaqGateway.sln ./
COPY src/ ./src/
COPY configs/ ./configs/
RUN dotnet publish src/Gateway.Host/Gateway.Host.csproj -c Release -o /app/publish --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .
# Default Fake demo config; Compose overlays this with a host-mounted YAML.
# Never COPY Fwlib*.dll / libfwlib32.so — vendor FOCAS binaries stay off image layers.
COPY configs/examples/gateway.docker.yaml /app/gateway.yaml
ENV GATEWAY_CONFIG=/app/gateway.yaml
# Runs as a normal container process — no industrial-PC / privileged assumption.
ENTRYPOINT ["dotnet", "Gateway.Host.dll"]
