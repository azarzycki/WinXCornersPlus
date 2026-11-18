namespace WinXCorners.Core.Input;

/// <summary>What a single hotkey token does to its key.</summary>
public enum KeyStepKind
{
    /// <summary>Bare <c>key</c>: key down, then key up.</summary>
    Press,

    /// <summary><c>_key</c>: key down; the key stays down until released.</summary>
    Hold,

    /// <summary><c>key_</c>: key up.</summary>
    Release
}

/// <summary>One parsed token of a hotkey sequence.</summary>
public readonly record struct KeyStep(ushort VirtualKey, KeyStepKind Kind);
