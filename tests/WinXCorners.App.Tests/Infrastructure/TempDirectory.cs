namespace WinXCorners.App.Tests.Infrastructure;

/// <summary>A unique folder under %TEMP% that is deleted (best effort) on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    internal TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wxc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    internal string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(200); // A child process may still be closing a file in it.
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }
}
