# Simplz.RTSP2HTTP

Minimal ASP.NET (.NET 10) service that pulls a single **RTSP** feed and re-serves
it as browser-playable **HLS** over HTTP. ffmpeg handles the RTSP→HLS repackaging
(stream-copy by default — no transcode, low CPU); Kestrel serves the playlist,
segments, and a tiny hls.js player.

```
RTSP camera  ──▶  ffmpeg (copy → HLS)  ──▶  ASP.NET /stream/*.m3u8  ──▶  browser
```

## Endpoints

| Path                  | Purpose                                  |
| --------------------- | ---------------------------------------- |
| `/`                   | Built-in hls.js player page              |
| `/stream/index.m3u8`  | Live HLS playlist                        |
| `/stream/seg_*.ts`    | HLS media segments                       |
| `/healthz`            | `200` once the first playlist is written |

## Configuration (environment variables)

| Variable          | Default        | Notes                                                        |
| ----------------- | -------------- | ----------------------------------------------------------- |
| `RTSP_URL`        | _(required)_   | Source URL, e.g. `rtsp://user:pass@camera-host:554/stream`  |
| `RTSP_TRANSPORT`  | `tcp`          | `tcp` (reliable) or `udp`                                   |
| `RTSP_TRANSCODE`  | `false`        | Set `true` to re-encode to H.264 if the source won't play   |
| `HLS_DIR`         | `%TMP%/hls`    | Where segments are written (`/tmp/hls` in the container)     |
| `ASPNETCORE_URLS` | `:8080` (Docker) | Listen address                                            |

## Run locally

Requires the .NET 10 SDK and `ffmpeg` on PATH.

```powershell
$env:RTSP_URL = "rtsp://user:pass@camera-host:554/stream"
dotnet run
# open http://localhost:5000  (or the port dotnet prints)
```

## Run with Docker

```bash
docker build -t simplz-rtsp2http .
docker run --rm -p 8080:8080 \
  -e RTSP_URL="rtsp://user:pass@camera-host:554/stream" \
  --tmpfs /tmp/hls \
  simplz-rtsp2http
```

Or with compose (edit `RTSP_URL` first):

```bash
docker compose up -d --build
```

## Deploy behind Caddy

The remote host already runs Caddy. Add the block from [`Caddyfile`](./Caddyfile)
to the server's Caddyfile, pointing `reverse_proxy` at the container
(`localhost:8090` with the compose port mapping), and set your real domain.
Caddy handles TLS automatically.

```bash
# after editing /etc/caddy/Caddyfile
caddy reload --config /etc/caddy/Caddyfile
```

## Notes

- **Audio** is dropped by default for cross-camera reliability. To keep it, add
  an audio codec (e.g. `-c:a aac`) in `RtspHlsService.cs` and remove `-an`.
- **Latency** is roughly `hls_time × a few segments` (~4–8 s). HLS trades latency
  for compatibility; for sub-second latency you'd switch to WebRTC/MSE, which is
  considerably less "minimal".
- Only the **most recent** segments are kept on disk (`delete_segments`), so the
  HLS dir stays small and fits comfortably in tmpfs/RAM.

## License

[MIT](./LICENSE)
