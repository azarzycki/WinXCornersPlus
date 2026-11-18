using System.Reflection;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Tests.Settings;

/// <summary>A unique, empty directory under the system temp folder, deleted on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wxc-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

/// <summary>A clock that only moves when told to.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class SettingsAssert
{
    /// <summary>Compares every public property, and custom commands element by element.</summary>
    public static void Equivalent(ApplicationSettings expected, ApplicationSettings actual)
    {
        foreach (var property in typeof(ApplicationSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name == nameof(ApplicationSettings.CustomCommands))
            {
                continue;
            }

            Assert.True(Equals(property.GetValue(expected), property.GetValue(actual)),
                $"{property.Name}: expected {property.GetValue(expected)}, actual {property.GetValue(actual)}");
        }

        Assert.Equal(expected.CustomCommands.Length, actual.CustomCommands.Length);
        for (var index = 0; index < expected.CustomCommands.Length; index++)
        {
            var left = expected.CustomCommands[index];
            var right = actual.CustomCommands[index];
            Assert.Equal(left.Name, right.Name);
            Assert.Equal(left.Command, right.Command);
            Assert.Equal(left.Parameters, right.Parameters);
            Assert.Equal(left.LaunchHidden, right.LaunchHidden);
        }
    }
}
