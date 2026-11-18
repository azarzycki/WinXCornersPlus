namespace WinXCorners.Core.Input;

/// <summary>Which windows a <see cref="WindowCondition"/> looks at.</summary>
public enum WindowConditionScope
{
    /// <summary><c>#[...]</c>: the foreground (focused) window.</summary>
    Foreground,

    /// <summary><c>@[...]</c>: any top-level window.</summary>
    AnyWindow
}

/// <summary>
/// One <c>#[class,title]</c> or <c>@[class,title]</c> condition. An empty <paramref name="ClassName"/> or
/// <paramref name="TitleText"/> means "not specified"; at least one of them is non-empty.
/// </summary>
public sealed record WindowCondition(WindowConditionScope Scope, string ClassName, string TitleText);

/// <summary>Class name and title of a window, as read by the app.</summary>
public readonly record struct WindowInfo(string ClassName, string Title);

/// <summary>Window lookups the app implements with Win32.</summary>
public interface IWindowQuery
{
    /// <summary>The foreground window, or null when there is none.</summary>
    WindowInfo? GetForegroundWindow();

    /// <summary>
    /// FindWindow semantics: true when a top-level window exists whose class equals
    /// <paramref name="className"/> and whose title equals <paramref name="title"/> exactly.
    /// A null argument is a wildcard.
    /// </summary>
    bool WindowExists(string? className, string? title);
}
