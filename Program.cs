using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Simplz.RTSP2HTTP;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostedService<RtspHlsService>();

var app = builder.Build();

// Make sure the HLS output directory exists before we try to serve from it.
Directory.CreateDirectory(RtspHlsService.HlsDirectory);

// Serve the built-in player (wwwroot/index.html) at "/".
app.UseDefaultFiles();
app.UseStaticFiles();

// Serve the live HLS playlist + segments at "/stream/*".
var hlsContentTypes = new FileExtensionContentTypeProvider();
hlsContentTypes.Mappings[".m3u8"] = "application/vnd.apple.mpegurl";
hlsContentTypes.Mappings[".ts"] = "video/mp2t";

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(RtspHlsService.HlsDirectory),
    RequestPath = "/stream",
    ContentTypeProvider = hlsContentTypes,
    ServeUnknownFileTypes = false,
    OnPrepareResponse = ctx =>
    {
        // Playlists must never be cached; segments are immutable.
        var headers = ctx.Context.Response.Headers;
        if (ctx.File.Name.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            headers.CacheControl = "no-cache, no-store, must-revalidate";
        else
            headers.CacheControl = "public, max-age=31536000, immutable";
    }
});

// Liveness/readiness: ready once ffmpeg has produced a playlist.
app.MapGet("/healthz", () =>
    File.Exists(Path.Combine(RtspHlsService.HlsDirectory, "index.m3u8"))
        ? Results.Ok("ready")
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.Run();
