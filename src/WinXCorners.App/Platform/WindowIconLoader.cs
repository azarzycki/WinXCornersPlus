using System.ComponentModel;

namespace WinXCorners.App;

/// <summary>Loads the app's icons from its embedded resources (falling back to the executable's own icon).</summary>
/// <remarks>Every method returns a new <see cref="Icon"/> that the caller owns and disposes.</remarks>
internal static class WindowIconLoader
{
    private const string ResourcePrefix = "Icons.";

    internal static Icon? TryLoadAppIcon()
    {
        return TryLoadEmbedded("app.ico") ?? TryLoadExecutableIcon();
    }

    internal static Icon? TryLoadTrayIcon(bool isLightTheme, bool hotCornersEnabled)
    {
        var theme = isLightTheme ? "light" : "dark";
        var state = hotCornersEnabled ? "enabled" : "disabled";
        return TryLoadEmbedded($"tray.ICON_{theme}_{state}.ico");
    }

    internal static Icon? TryLoadAboutIcon()
    {
        return TryLoadEmbedded("about.ico") ?? TryLoadAppIcon();
    }

    internal static Icon LoadFallbackTrayIcon()
    {
        return TryLoadEmbedded("WinXCornersPlus.ico") ?? TryLoadAppIcon() ?? (Icon)SystemIcons.Application.Clone();
    }

    private static Icon? TryLoadEmbedded(string resourceName)
    {
        using var resourceStream = typeof(WindowIconLoader).Assembly.GetManifestResourceStream(ResourcePrefix + resourceName);
        if (resourceStream is null)
        {
            return null;
        }

        try
        {
            // Copy first: Icon keeps reading from the stream it was given.
            using var buffer = new MemoryStream();
            resourceStream.CopyTo(buffer);
            buffer.Position = 0;
            using var icon = new Icon(buffer);
            return (Icon)icon.Clone();
        }
        catch (ArgumentException)
        {
            return null; // Not a valid icon.
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static Icon? TryLoadExecutableIcon()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            return Icon.ExtractAssociatedIcon(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
