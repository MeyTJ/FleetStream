# Multi-stage build for FleetStream BFF API
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/Core/FleetStream.Core.csproj", "src/Core/"]
COPY ["src/Application/FleetStream.Application.csproj", "src/Application/"]
COPY ["src/Infrastructure/FleetStream.Infrastructure.csproj", "src/Infrastructure/"]
COPY ["src/Presentation/FleetStream.Presentation.csproj", "src/Presentation/"]

# Restore dependencies
RUN dotnet restore "src/Presentation/FleetStream.Presentation.csproj"

# Copy all source code
COPY . .

# Build
WORKDIR "/src/src/Presentation"
RUN dotnet build "FleetStream.Presentation.csproj" -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "FleetStream.Presentation.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create non-root user
RUN groupadd -r fleetstream && useradd -r -g fleetstream fleetstream
USER fleetstream

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget --no-verbose --tries=1 --spider http://localhost:8080/api/v1/health/live || exit 1

ENTRYPOINT ["dotnet", "FleetStream.Presentation.dll"]
