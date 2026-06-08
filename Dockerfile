# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Simplz.RTSP.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# ffmpeg does the RTSP -> HLS work; curl is used by the healthcheck.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Write HLS segments to tmpfs-friendly path; Kestrel listens on 8080.
ENV HLS_DIR=/tmp/hls \
    ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=5 \
    CMD ["bash", "-c", "curl -fsS http://localhost:8080/healthz || exit 1"]

ENTRYPOINT ["dotnet", "Simplz.RTSP.dll"]
