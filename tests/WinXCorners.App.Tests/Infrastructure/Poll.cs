using System.Diagnostics;

namespace WinXCorners.App.Tests.Infrastructure;

internal static class Poll
{
    /// <summary>Waits (without pumping) until <paramref name="condition"/> holds; false when the time is up.</summary>
    internal static bool Until(Func<bool> condition, TimeSpan timeout, int intervalMs = 50)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > timeout)
            {
                return false;
            }

            Thread.Sleep(intervalMs);
        }

        return true;
    }

    /// <summary>Reads a file another process is writing; null until it exists and can be read with content.</summary>
    internal static string? TryReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            return text.Length == 0 ? null : text;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Waits for a marker file with content (written by a child process).</summary>
    internal static string? WaitForFile(string path, TimeSpan timeout)
    {
        string? text = null;
        Until(() => (text = TryReadFile(path)) is not null, timeout, 100);
        return text;
    }
}
