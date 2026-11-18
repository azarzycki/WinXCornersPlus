using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinXCorners.App;

/// <summary>Process-wide mitigations an elevated instance turns on before it touches the file system.</summary>
internal static class ProcessMitigations
{
    /// <summary>ERROR_INVALID_PARAMETER: what builds without the policy return.</summary>
    private const int ErrorInvalidParameter = 87;

    /// <summary>
    /// Turns on Redirection Guard (enforced): from now on this process does not follow junctions that were created
    /// by non-administrators (review V2 D1). The settings folder lives in the user's profile, where any program
    /// running as the user can replace it with a junction; an elevated process following one would read and write
    /// files wherever it points with the administrator's token. Windows builds before 10 21H2 / Server 2022 do not
    /// have the policy; that is reported, not fatal, because the settings store's own handle checks still apply.
    /// </summary>
    /// <returns>A line for the log saying what happened.</returns>
    internal static string EnableRedirectionGuard()
    {
        var flags = NativeMethods.RedirectionTrustEnforce;
        if (NativeMethods.SetProcessMitigationPolicy(NativeMethods.ProcessRedirectionTrustPolicy, ref flags, (UIntPtr)sizeof(uint)))
        {
            return "Redirection Guard is on: junctions created by non-administrators are not followed.";
        }

        var error = Marshal.GetLastPInvokeError();
        return error == ErrorInvalidParameter
            ? "Redirection Guard is not available on this Windows build; the settings checks still refuse redirected locations."
            : $"Redirection Guard could not be turned on ({new Win32Exception(error).Message}); the settings checks still refuse redirected locations.";
    }

    /// <summary>True when Redirection Guard is enforced for this process (for tests and diagnostics).</summary>
    internal static bool IsRedirectionGuardEnforced()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return NativeMethods.GetProcessMitigationPolicy(process.Handle, NativeMethods.ProcessRedirectionTrustPolicy, out var flags, (UIntPtr)sizeof(uint)) &&
               (flags & NativeMethods.RedirectionTrustEnforce) != 0;
    }
}
