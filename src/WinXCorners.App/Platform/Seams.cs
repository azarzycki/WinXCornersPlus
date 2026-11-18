using WinXCorners.Core.Actions;
using WinXCorners.Core.Input;
using WinXCorners.Core.Security;

namespace WinXCorners.App;

// Small interfaces through which ActionExecutor reaches every side effect, so tests can substitute fakes
// (a test must never really lock the CI machine's workstation or type keys into it unintentionally).

/// <summary>Injects keyboard events and reports which physical modifier keys are held.</summary>
internal interface IKeySender
{
    void Send(IReadOnlyList<KeyEvent> events);

    IReadOnlyList<ushort> GetModifiersDown();
}

/// <summary>Starts a program, document or link as Core's <see cref="LaunchPlanner"/> plans it.</summary>
internal interface IProcessLauncher
{
    bool TryLaunch(LaunchRequest request, out string? error);
}

/// <summary>The session-level commands: WM_SYSCOMMAND requests to the app's own window and LockWorkStation.</summary>
internal interface ISystemActions
{
    bool TryPostSystemCommand(IntPtr window, SystemCommandKind kind, out string? error);

    bool TryLockWorkstation(out string? error);
}
