# syntax=docker/dockerfile:1
# ── Stage 1: Build & Publish Backend ──────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /app

# Copy solution, CPM definitions, and project definitions for caching restore
COPY Directory.Build.props Directory.Packages.props ./
COPY MailStripper/*.csproj MailStripper/
RUN dotnet restore MailStripper/MailStripper.csproj

# Copy source code and build
COPY MailStripper/ MailStripper/
WORKDIR /app/MailStripper
RUN dotnet publish -c Release -o /app/publish

# ── Stage 2: Hardened Runtime ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Install wget for container health checks
RUN apk add --no-cache wget

# Copy published application
COPY --from=build /app/publish .

# Application configuration
ENV ASPNETCORE_URLS=http://+:5001
ENV ASPNETCORE_ENVIRONMENT=Production

USER $APP_UID

EXPOSE 5001

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://localhost:5001/api/strip/status || exit 1

ENTRYPOINT ["dotnet", "MailStripper.dll"]
