using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Security;

public class ElevationModeTests
{
    [Theory]
    [InlineData(false, TokenElevationType.Default, ElevationMode.NotElevated)]
    [InlineData(false, TokenElevationType.Full, ElevationMode.NotElevated)]
    [InlineData(false, TokenElevationType.Limited, ElevationMode.NotElevated)]
    [InlineData(false, TokenElevationType.Unknown, ElevationMode.NotElevated)]
    // A split token: a normal-rights shell exists for this sign-in.
    [InlineData(true, TokenElevationType.Full, ElevationMode.ElevatedWithStandardShell)]
    // No split token while elevated: UAC off or the built-in Administrator; everything runs elevated.
    [InlineData(true, TokenElevationType.Default, ElevationMode.ElevatedWithoutStandardRights)]
    // Impossible or unreadable combinations take the path that checks the shell and fails closed.
    [InlineData(true, TokenElevationType.Limited, ElevationMode.ElevatedWithStandardShell)]
    [InlineData(true, TokenElevationType.Unknown, ElevationMode.ElevatedWithStandardShell)]
    [InlineData(true, (TokenElevationType)99, ElevationMode.ElevatedWithStandardShell)]
    public void Classify_covers_every_token_type(bool isElevated, TokenElevationType type, ElevationMode expected)
    {
        Assert.Equal(expected, ElevationModes.Classify(isElevated, type));
    }

    [Theory]
    [InlineData(ElevationMode.NotElevated, false, ElevationMode.ElevatedWithStandardShell)]
    [InlineData(ElevationMode.ElevatedWithStandardShell, true, ElevationMode.ElevatedWithStandardShell)]
    [InlineData(ElevationMode.ElevatedWithoutStandardRights, true, ElevationMode.ElevatedWithoutStandardRights)]
    public void IsElevated_and_AfterElevation(ElevationMode mode, bool elevated, ElevationMode after)
    {
        Assert.Equal(elevated, mode.IsElevated());
        Assert.Equal(after, mode.AfterElevation());
    }

    [Fact]
    public void Token_type_values_are_the_Windows_values()
    {
        // GetTokenInformation(TokenElevationType) returns these numbers; the app casts them straight through.
        Assert.Equal(1, (int)TokenElevationType.Default);
        Assert.Equal(2, (int)TokenElevationType.Full);
        Assert.Equal(3, (int)TokenElevationType.Limited);
    }

    [Theory]
    [InlineData(false, false, ElevationMode.NotElevated)]
    [InlineData(false, true, ElevationMode.NotElevated)]
    [InlineData(true, false, ElevationMode.ElevatedWithStandardShell)]
    [InlineData(true, true, ElevationMode.ElevatedWithoutStandardRights)]
    public void SecurityContext_maps_its_two_facts_onto_the_three_states(bool elevated, bool everything, ElevationMode expected)
    {
        var context = new SecurityContext(elevated, TestData.ProtectedInstall, false, "x", everything);
        Assert.Equal(expected, context.Elevation);
    }
}
