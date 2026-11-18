namespace WinXCorners.Core.Platform;

/// <summary>
/// Well-known folders as reported by the OS (the app reads them with <c>Environment.GetFolderPath</c>). Core never
/// guesses these; it only reasons about the values it is given.
/// </summary>
/// <param name="Windows">For example <c>C:\Windows</c>.</param>
/// <param name="System32">For example <c>C:\Windows\System32</c>.</param>
/// <param name="UserProfile">For example <c>C:\Users\alex</c>; used as the working directory for user launches.</param>
/// <param name="ProtectedRoots">Folders only administrators can change: Program Files, Program Files (x86), Windows.</param>
public sealed record SystemDirectories(string Windows, string System32, string UserProfile, IReadOnlyList<string> ProtectedRoots);
