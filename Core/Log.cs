using System;
using System.Diagnostics;
using System.IO;

namespace FrozenOCR.Core;

internal static class Log
{
    private static readonly object _lock = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Memory(string scope)
    {
#if DEBUG
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var workingSetMb = process.WorkingSet64 / (1024.0 * 1024.0);
            var privateMb = process.PrivateMemorySize64 / (1024.0 * 1024.0);
            var managedMb = GC.GetTotalMemory(forceFullCollection: false) / (1024.0 * 1024.0);
            Write("MEM", $"{scope} | workingSetMB={workingSetMb:F1} | privateMB={privateMb:F1} | managedMB={managedMb:F1}");
        }
        catch
        {
            // never crash due to logging
        }
#endif
    }

    private static void Write(string level, string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FrozenOCR"
            );
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, "log.txt");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

            lock (_lock)
            {
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // never crash due to logging
        }
    }
}
