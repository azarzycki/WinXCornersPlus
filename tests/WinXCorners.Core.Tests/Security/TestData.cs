using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Security;

internal static class TestData
{
    public static SystemDirectories Directories { get; } = new(
        @"C:\Windows",
        @"C:\Windows\System32",
        @"C:\Users\alex",
        [@"C:\Program Files", @"C:\Program Files (x86)", @"C:\Windows"]);

    public static InstallLocationAssessment ProtectedInstall { get; } =
        new(InstallLocationKind.Protected, @"C:\Program Files\WinXCornersPlus", "protected");

    public static InstallLocationAssessment WritableInstall { get; } =
        new(InstallLocationKind.UserWritable, @"C:\Users\alex\Downloads\WinXCornersPlus", "writable");

    public static InstallLocationAssessment UnknownInstall { get; } =
        new(InstallLocationKind.Unknown, string.Empty, "unknown");
}
