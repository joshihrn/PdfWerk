# syntax=docker/dockerfile:1

# ---- stage 1: build the web UI and the embeddable widget --------------------
FROM node:22-alpine AS web
WORKDIR /web

# Copied first so a source-only change does not invalidate the dependency layer.
COPY web/package*.json ./
RUN npm ci

COPY web/ ./
RUN npm run build:all

# ---- stage 2: build and publish the API ------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS api
WORKDIR /src

COPY *.sln Directory.Build.props ./
COPY src/PdfWerk.Core/*.csproj           src/PdfWerk.Core/
COPY src/PdfWerk.Pdf/*.csproj            src/PdfWerk.Pdf/
COPY src/PdfWerk.Ai/*.csproj             src/PdfWerk.Ai/
COPY src/PdfWerk.Infrastructure/*.csproj src/PdfWerk.Infrastructure/
COPY src/PdfWerk.Api/*.csproj            src/PdfWerk.Api/
COPY tests/PdfWerk.Tests/*.csproj        tests/PdfWerk.Tests/
RUN dotnet restore

COPY src/ src/
COPY tests/ tests/

# The UI is published into wwwroot so one container serves both.
COPY --from=web /src/PdfWerk.Api/wwwroot/ src/PdfWerk.Api/wwwroot/

RUN dotnet publish src/PdfWerk.Api/PdfWerk.Api.csproj -c Release -o /app --no-restore

# ---- stage 3: runtime -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# LibreOffice gives high-fidelity Word conversion; the managed converter is the fallback when
# it is absent, so this layer can be dropped for a much smaller image if fidelity is not needed.
#
# The fonts matter independently: PDFsharp ships none and has no default resolver on Linux, so
# without them every rendered document would fail to find a typeface.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      libreoffice-writer-nogui \
      fonts-dejavu-core \
      fonts-liberation \
 && rm -rf /var/lib/apt/lists/*

# Runs unprivileged: this service parses untrusted files from the public internet.
RUN useradd --create-home --shell /usr/sbin/nologin pdfwerk \
 && mkdir -p /data && chown pdfwerk:pdfwerk /data

COPY --from=api --chown=pdfwerk:pdfwerk /app ./
USER pdfwerk

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__Sqlite="Data Source=/data/pdfwerk.db" \
    HOME=/home/pdfwerk

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8080/health || exit 1"]

ENTRYPOINT ["dotnet", "PdfWerk.Api.dll"]
