using System.IO;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.Services;

public sealed class CacheInspector : ICacheInspector
{
    public Task<(long Bytes, int Files)> GetDirectorySizeAsync(
        string path,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return (0L, 0);
            }

            long bytes = 0;
            var files = 0;
            var pending = new Stack<string>();
            pending.Push(path);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = pending.Pop();

                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = new DirectoryInfo(current).EnumerateFileSystemInfos();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // Do not follow junctions or symlinks, otherwise cache scans can loop or escape the cache.
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        if (entry is DirectoryInfo directory)
                        {
                            pending.Push(directory.FullName);
                        }
                        else if (entry is FileInfo file)
                        {
                            bytes += file.Length;
                            files++;

                            if (files % 256 == 0)
                            {
                                progress?.Report(bytes);
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // A file disappeared while scanning; keep going.
                    }
                }
            }

            progress?.Report(bytes);
            return (bytes, files);
        }, cancellationToken);
}
