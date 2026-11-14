using System.IO;

namespace RenoDXMfgUnlocker;

internal sealed class ReShadeSessionLog
{
    private readonly string path;
    private readonly string session = Guid.NewGuid().ToString("N")[..8];
    private static readonly object Gate = new();
    internal static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenoDX MFG Unlocker", "Diagnostics");

    internal ReShadeSessionLog(string? folder = null) => path = Path.Combine(folder ?? DirectoryPath, "ReShade-lifecycle.log");

    internal void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                var root = Path.GetPathRoot(path)!;
                StreamlineStagingEvidenceService.EnsureNoReparsePoints(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (new FileInfo(path) is { Exists:true, Length: > 256 * 1024 })
                {
                    StreamlineStagingEvidenceService.EnsureNoReparsePoints(root, path + ".previous");
                    File.Move(path, path + ".previous", true);
                }
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} app={Environment.ProcessId} session={session} {message}{Environment.NewLine}");
            }
            catch (Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                System.Diagnostics.Trace.TraceWarning("ReShade lifecycle log unavailable: {0}", ex.GetType().Name);
            }
        }
    }
}
