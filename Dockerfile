# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /src

# Copy project files for layer-cached restore
COPY ArbiScanner.McpServer.Domain/*.csproj ArbiScanner.McpServer.Domain/
COPY ArbiScanner.McpServer.Abstractions/*.csproj ArbiScanner.McpServer.Abstractions/
COPY ArbiScanner.McpServer.Infrastructure/*.csproj ArbiScanner.McpServer.Infrastructure/
COPY ArbiScanner.McpServer.Host/*.csproj ArbiScanner.McpServer.Host/

RUN dotnet restore ArbiScanner.McpServer.Host/ArbiScanner.McpServer.Host.csproj

# Copy full source and publish
COPY . .
RUN dotnet publish ArbiScanner.McpServer.Host/ArbiScanner.McpServer.Host.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build-env /app/publish .
USER app
ENTRYPOINT ["dotnet", "ArbiScanner.McpServer.Host.dll"]
