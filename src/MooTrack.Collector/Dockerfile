# Packaging only. The build context is a published output directory, produced and
# tested by CI, so the bits that shipped are the bits that were tested.
#
#   dotnet publish src/MooTrack.Collector/MooTrack.Collector.csproj -c Release -o artifacts/collector
#   docker build -f src/MooTrack.Collector/Dockerfile -t mootrack-collector artifacts/collector
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine

WORKDIR /app
COPY . ./

# ClosedXML measures column widths through SixLabors.Fonts, which needs font files
# on disk; without them workbook generation throws at runtime.
RUN apk add --no-cache fontconfig ttf-dejavu \
    && mkdir -p /data/raw /data/reports /mirror/raw \
    && chown -R "$APP_UID":"$APP_UID" /data /mirror

ENV ASPNETCORE_HTTP_PORTS=8080 \
    MooTrack__RawRoot=/data/raw \
    MooTrack__ReportRoot=/data/reports \
    MooTrack__MirrorRoot=/mirror/raw

EXPOSE 8080
USER $APP_UID

HEALTHCHECK --interval=60s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -qO- http://127.0.0.1:8080/health >/dev/null || exit 1

ENTRYPOINT ["dotnet", "MooTrack.Collector.dll"]
