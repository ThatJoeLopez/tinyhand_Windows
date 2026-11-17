using System;
using System.Diagnostics;
using System.IO;

namespace TinyHand;

internal static class Log
{
    private static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(Paths.LocalData, "TinyHand.log");

    public static void Write(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Paths.LocalData);
                var info = new FileInfo(Path);
                if (info.Exists && info.Length > 2_000_000)
                {
                    File.Move(Path, Path + ".old", overwrite: true);
                }
                File.AppendAllText(Path, line);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        Debug.Write(line);
    }
}

internal static class Paths
{
    /// %APPDATA%\TinyHand — settings, custom hands and the automation state file.
    public static string Data { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TinyHand");

    /// %LOCALAPPDATA%\TinyHand — the log.
    public static string LocalData { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinyHand");

    public static string AppDir { get; } = AppContext.BaseDirectory;

    public static string ExePath { get; } = Environment.ProcessPath ?? System.IO.Path.Combine(AppDir, "TinyHand.exe");

    /// Looks for a bundled asset next to the exe, then in the repository (for `dotnet run`).
    public static string? Asset(string relative)
    {
        string[] candidates =
        {
            System.IO.Path.Combine(AppDir, "assets", relative),
            System.IO.Path.Combine(AppDir, relative),
            System.IO.Path.Combine(AppDir, "..", "..", "..", "..", "..", "assets", relative),
        };
        foreach (string c in candidates)
        {
            if (File.Exists(c))
            {
                return System.IO.Path.GetFullPath(c);
            }
        }
        return null;
    }
}
