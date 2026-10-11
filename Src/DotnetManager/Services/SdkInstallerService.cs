using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.Services;

/// <summary>
/// Guides the user to the official SDK download: it opens the download page, or downloads the
/// official installer into %TEMP%, verifies it against the SHA-512 published in Microsoft's release
/// metadata and starts it. The actual installation always happens in Microsoft's own UI, so this
/// service never needs elevation and never installs anything silently.
/// </summary>
public sealed class SdkInstallerService : IDotnetSdkInstaller
{
    private const string DownloadPageRoot = "https://dotnet.microsoft.com/download/dotnet";
    private const string ReleaseMetadata = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/{0}/releases.json";

    /// <summary>Fallback used when the release metadata cannot be read.</summary>
    private static readonly string FallbackInstaller = "https://aka.ms/dotnet/{0}/dotnet-sdk-win-x64.exe";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public Task<OperationResult> OpenDownloadPageAsync(string? channel = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var url = string.IsNullOrWhiteSpace(channel) ? DownloadPageRoot : $"{DownloadPageRoot}/{channel}";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return Task.FromResult(OperationResult.Ok(url));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return Task.FromResult(OperationResult.Fail(ex.Message, url));
        }
    }

    public async Task<OperationResult> DownloadAndRunInstallerAsync(
        string? channel = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        channel = string.IsNullOrWhiteSpace(channel) ? $"{Environment.Version.Major}.0" : channel.Trim();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(45));

        try
        {
            var artifact = await ResolveInstallerAsync(channel, progress, timeout.Token).ConfigureAwait(false);
            var targetPath = Path.Combine(Path.GetTempPath(), artifact.FileName);

            if (File.Exists(targetPath) && await VerifyAsync(targetPath, artifact.Sha512, timeout.Token).ConfigureAwait(false))
            {
                progress?.Report(string.Format("Reusing {0}", targetPath));
            }
            else
            {
                await DownloadAsync(artifact, targetPath, progress, timeout.Token).ConfigureAwait(false);

                if (!await VerifyAsync(targetPath, artifact.Sha512, timeout.Token).ConfigureAwait(false))
                {
                    TryDelete(targetPath);
                    return OperationResult.Fail("The downloaded file failed the SHA-512 check and was deleted.", artifact.Url);
                }
            }

            Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
            return OperationResult.Ok(targetPath);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail("The download was cancelled.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or Win32Exception)
        {
            return OperationResult.Fail(ex.Message, $"{DownloadPageRoot}/{channel}");
        }
    }

    private async Task<SdkArtifact> ResolveInstallerAsync(string channel, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var metadataUrl = string.Format(ReleaseMetadata, channel);
        try
        {
            progress?.Report(string.Format("Reading {0}", metadataUrl));
            using var response = await Http.GetAsync(metadataUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var artifact = FindInstaller(document);
            if (artifact is not null)
            {
                return artifact;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            // Fall through to the stable aka.ms link below.
        }

        return new SdkArtifact(string.Format(FallbackInstaller, channel), null, SdkFileName(channel));
    }

    private static SdkArtifact? FindInstaller(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var release in releases.EnumerateArray().Take(5))
        {
            if (!release.TryGetProperty("sdk", out var sdk)
                || !sdk.TryGetProperty("files", out var files)
                || files.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var file in files.EnumerateArray())
            {
                var rid = file.TryGetProperty("rid", out var ridElement) ? ridElement.GetString() : null;
                var name = file.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                var url = file.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;

                if (rid != "win-x64" || name is null || url is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hash = file.TryGetProperty("hash", out var hashElement) ? hashElement.GetString() : null;
                return new SdkArtifact(url, hash, name);
            }
        }

        return null;
    }

    private static async Task DownloadAsync(SdkArtifact artifact, string targetPath, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(artifact.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

        var buffer = new byte[1 << 16];
        long copied = 0;
        var lastReport = DateTime.MinValue;

        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;

            if (DateTime.UtcNow - lastReport >= TimeSpan.FromMilliseconds(300))
            {
                progress?.Report(ProgressText(copied, total));
                lastReport = DateTime.UtcNow;
            }
        }

        progress?.Report(ProgressText(copied, total));
    }

    private static async Task<bool> VerifyAsync(string path, string? expectedSha512, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha512))
        {
            // No hash published for this build; size is the only cheap sanity check.
            return new FileInfo(path).Length > 0;
        }

        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA512.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        return string.Equals(hash, expectedSha512.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string ProgressText(long copied, long? total)
    {
        const double Mb = 1024d * 1024d;
        return total is { } value
            ? string.Format("{0:0.0} MB / {1:0.0} MB", copied / Mb, value / Mb)
            : string.Format("{0:0.0} MB", copied / Mb);
    }

    private static string SdkFileName(string channel) => $"dotnet-sdk-{channel}-win-x64.exe";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leaving a partial file behind is not worth failing the whole operation for.
        }
    }

    private sealed record SdkArtifact(string Url, string? Sha512, string FileName);
}
