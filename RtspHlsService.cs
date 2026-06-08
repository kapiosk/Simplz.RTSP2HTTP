using System.Diagnostics;

namespace Simplz.RTSP2HTTP;

/// <summary>
/// Pulls an RTSP feed with ffmpeg and repackages it as HLS on disk.
/// By default the video stream is copied (no transcode) for minimal CPU use;
/// set RTSP_TRANSCODE=true to re-encode for cameras whose codec the browser
/// can't play natively. ffmpeg is supervised and restarted on exit.
/// </summary>
public sealed class RtspHlsService(IConfiguration config, ILogger<RtspHlsService> log) : BackgroundService
{
    public static readonly string HlsDirectory =
        Environment.GetEnvironmentVariable("HLS_DIR") ?? Path.Combine(Path.GetTempPath(), "hls");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rtspUrl = config["RTSP_URL"];
        if (string.IsNullOrWhiteSpace(rtspUrl))
        {
            log.LogCritical("RTSP_URL is not configured. Set the RTSP_URL environment variable.");
            return;
        }

        Directory.CreateDirectory(HlsDirectory);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunFfmpegAsync(rtspUrl, stoppingToken);
                log.LogWarning("ffmpeg exited; restarting in 3s.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "ffmpeg failed; restarting in 3s.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunFfmpegAsync(string rtspUrl, CancellationToken ct)
    {
        var transcode = config.GetValue("RTSP_TRANSCODE", false);
        var transport = config["RTSP_TRANSPORT"] ?? "tcp"; // tcp is far more reliable than udp
        var playlist = Path.Combine(HlsDirectory, "index.m3u8");
        var segments = Path.Combine(HlsDirectory, "seg_%05d.ts");

        var args = new List<string>
        {
            "-nostdin", "-hide_banner", "-loglevel", "warning",
            "-rtsp_transport", transport,
            "-fflags", "nobuffer",
            "-i", rtspUrl,
        };

        if (transcode)
            args.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-tune", "zerolatency", "-g", "50"]);
        else
            args.AddRange(["-c:v", "copy"]);

        // Audio off by default to keep things simple/reliable across cameras.
        args.Add("-an");

        args.AddRange([
            "-f", "hls",
            "-hls_time", "2",
            "-hls_list_size", "6",
            "-hls_flags", "delete_segments+append_list+omit_endlist",
            "-hls_segment_filename", segments,
            playlist,
        ]);

        var psi = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        log.LogInformation("Starting ffmpeg ({Mode}) for RTSP feed via {Transport}.",
            transcode ? "transcode" : "copy", transport);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.LogDebug("ffmpeg: {Line}", e.Data); };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) log.LogDebug("ffmpeg: {Line}", e.Data); };

        proc.Start();
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }
    }

    private void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
        catch (Exception ex) { log.LogWarning(ex, "Failed to kill ffmpeg."); }
    }
}
