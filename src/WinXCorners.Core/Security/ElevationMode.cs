namespace WinXCorners.Core.Security;

/// <summary>
/// The kind of token Windows reports for a process (<c>TOKEN_ELEVATION_TYPE</c>). The numeric values are Windows'
/// own, so the app can pass what <c>GetTokenInformation(TokenElevationType)</c> returns straight through.
/// </summary>
public enum TokenElevationType
{
    /// <summary>The query failed or returned a value this version does not know.</summary>
    Unknown = 0,

    /// <summary>
    /// No split token: a standard user, or an administrator for whom User Account Control does not filter the token
    /// (UAC turned off, or the built-in Administrator account). Everything such an administrator runs is elevated.
    /// </summary>
    Default = 1,

    /// <summary>The elevated half of a split token: a filtered, normal-rights token exists for the same sign-in.</summary>
    Full = 2,

    /// <summary>The filtered, normal-rights half of a split token.</summary>
    Limited = 3
}

/// <summary>
/// What "elevated" means for what the app starts (review V2 D4). Three states, because "elevated" alone does not say
/// whether a normal-rights context exists to start programs in.
/// </summary>
public enum ElevationMode
{
    /// <summary>The app runs with normal rights; what it starts gets the same rights.</summary>
    NotElevated,

    /// <summary>
    /// The app is elevated and User Account Control keeps a normal-rights token for this sign-in, so the desktop shell
    /// normally runs without administrator rights. Programs are started through the shell; if the shell cannot be
    /// reached, or turns out to run as administrator itself, nothing is started (fail closed).
    /// </summary>
    ElevatedWithStandardShell,

    /// <summary>
    /// The app is elevated and this sign-in has no normal-rights context at all (User Account Control is off, or this
    /// is the built-in Administrator account): the desktop shell and everything else on this desktop already runs as
    /// administrator. Programs are started directly; going through the shell would give them the same rights, so
    /// there is nothing to protect, and the user is told plainly that they get administrator rights.
    /// </summary>
    ElevatedWithoutStandardRights
}

public static class ElevationModes
{
    /// <summary>
    /// Classifies the running process from two facts the app reads from its own token. An elevated token of an
    /// unexpected type is treated as having a standard shell: that path checks the shell's own token and fails
    /// closed, so the uncertainty can only make a launch fail, never give a program administrator rights.
    /// </summary>
    public static ElevationMode Classify(bool isElevated, TokenElevationType tokenType)
    {
        if (!isElevated)
        {
            return ElevationMode.NotElevated;
        }

        return tokenType == TokenElevationType.Default
            ? ElevationMode.ElevatedWithoutStandardRights
            : ElevationMode.ElevatedWithStandardShell;
    }

    /// <summary>True for both elevated states.</summary>
    public static bool IsElevated(this ElevationMode mode) => mode != ElevationMode.NotElevated;

    /// <summary>
    /// The state an administrator session started from this one will be in, for text shown before the UAC prompt.
    /// A process that is not elevated can only get there through a UAC prompt, and User Account Control being on
    /// means a normal-rights shell exists; an elevated process already knows its state.
    /// </summary>
    public static ElevationMode AfterElevation(this ElevationMode current) =>
        current == ElevationMode.NotElevated ? ElevationMode.ElevatedWithStandardShell : current;
}
