using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinXCorners.Core.Settings;

namespace WinXCorners.App;

/// <summary>
/// settings.json access for an elevated session (review V2 D1), with every check made on handles rather than on path
/// strings, so that nothing a normal-rights program does to the folder between a check and a use can redirect it.
/// </summary>
/// <remarks>
/// <para>The chain is opened one level at a time:</para>
/// <list type="number">
/// <item>The parent of the settings folder (%APPDATA%) is opened by path without following a final reparse point. It
/// must be a real directory, and the final path of the handle (which resolves every junction and symbolic link above
/// it) must be the expected path: a redirected ancestor changes the final path.</item>
/// <item>The settings folder is opened (or created) relative to that handle with <c>FILE_OPEN_REPARSE_POINT</c>, so a
/// junction or symbolic link in its place is opened as itself, seen and refused, never followed. A relative open
/// cannot be redirected by renaming or replacing anything above it afterwards: the handle is the verified directory.</item>
/// <item>settings.json is opened (or created) relative to the folder handle the same way, and must be a regular file
/// with exactly one link (a hard link would make an "in place" write land in another file too) whose final path is
/// the expected one.</item>
/// </list>
/// <para>Writes go to that handle in place. A temp file plus rename would need two more name operations in a folder
/// other programs control; the in-place write needs none, at the cost of a torn file if power fails mid-write, which
/// the next normal-rights start detects and moves aside. Redirection Guard (see <see cref="ProcessMitigations"/>) is
/// a second layer in the real process; these checks do not rely on it.</para>
/// </remarks>
internal sealed class VerifiedSettingsFile : IVerifiedSettingsFile
{
    private const uint FileGenericRead = 0x00120089;
    private const uint FileGenericWrite = 0x00120116;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    private readonly string _rootPath;
    private readonly string _folderName;
    private readonly string _fileName;
    private readonly string _folderPath;
    private readonly string _filePath;

    /// <param name="settingsFilePath">Full path of settings.json, e.g. %APPDATA%\WinXCornersPlus\settings.json.</param>
    internal VerifiedSettingsFile(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        _filePath = Path.GetFullPath(settingsFilePath);
        _folderPath = Path.GetDirectoryName(_filePath) ?? throw new ArgumentException("The settings file has no folder.", nameof(settingsFilePath));
        _rootPath = Path.GetDirectoryName(_folderPath) ?? throw new ArgumentException("The settings folder has no parent.", nameof(settingsFilePath));
        _fileName = Path.GetFileName(_filePath);
        _folderName = Path.GetFileName(_folderPath);
    }

    public VerifiedRead Read(int maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        var chain = OpenChain(forWriting: false);
        using (chain)
        {
            if (chain.File is null)
            {
                return chain.Result;
            }

            try
            {
                using var stream = new FileStream(chain.File, FileAccess.Read, bufferSize: 0);
                var buffer = new byte[maxBytes];
                var length = 0;
                int read;
                while (length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) > 0)
                {
                    length += read;
                }

                return new VerifiedRead(VerifiedReadStatus.Read, buffer[..length]);
            }
            catch (IOException ex)
            {
                return new VerifiedRead(VerifiedReadStatus.Failed, Error: ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return new VerifiedRead(VerifiedReadStatus.Failed, Error: ex.Message);
            }
        }
    }

    public string? Write(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var chain = OpenChain(forWriting: true);
        using (chain)
        {
            if (chain.File is null)
            {
                return chain.Result.Status == VerifiedReadStatus.Missing
                    ? $"the folder {_rootPath} does not exist."
                    : chain.Result.Error ?? "the settings file could not be opened.";
            }

            try
            {
                using var stream = new FileStream(chain.File, FileAccess.ReadWrite, bufferSize: 0);
                // New content first, then cut the old tail: the file is never shorter than it has to be.
                stream.Position = 0;
                stream.Write(content);
                stream.SetLength(content.Length);
                stream.Flush(flushToDisk: true);
                return null;
            }
            catch (IOException ex)
            {
                return ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                return ex.Message;
            }
        }
    }

    /// <summary>Opens root, folder and file, verifying each; on any problem returns no file and why.</summary>
    private Chain OpenChain(bool forWriting)
    {
        var chain = new Chain();
        var rootAccess = NativeMethods.FILE_READ_ATTRIBUTES | NativeMethods.FILE_TRAVERSE | NativeMethods.SYNCHRONIZE |
                         (forWriting ? NativeMethods.FILE_ADD_SUBDIRECTORY : 0);
        chain.Root = NativeMethods.CreateFile(_rootPath, rootAccess,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE, IntPtr.Zero,
            NativeMethods.OPEN_EXISTING, NativeMethods.FILE_FLAG_BACKUP_SEMANTICS | NativeMethods.FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (chain.Root.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            chain.Result = error is ErrorFileNotFound or ErrorPathNotFound
                ? new VerifiedRead(VerifiedReadStatus.Missing)
                : Failed($"the folder {_rootPath} could not be opened: {new Win32Exception(error).Message}");
            return chain;
        }

        var problem = CheckDirectory(chain.Root, _rootPath) ?? CheckFinalPath(chain.Root, ExpectedLongPath(_rootPath), _rootPath);
        if (problem is not null)
        {
            chain.Result = Refused(problem);
            return chain;
        }

        var rootFinal = FinalPath(chain.Root)!;
        var folderAccess = NativeMethods.FILE_READ_ATTRIBUTES | NativeMethods.FILE_TRAVERSE | NativeMethods.SYNCHRONIZE |
                           (forWriting ? NativeMethods.FILE_ADD_FILE : 0);
        var status = OpenRelative(chain.Root, _folderName, folderAccess,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
            forWriting ? NativeMethods.FILE_OPEN_IF : NativeMethods.FILE_OPEN, NativeMethods.FILE_DIRECTORY_FILE, out var folder);
        chain.Folder = folder;
        if (status != 0)
        {
            chain.Result = OpenFailed(status, _folderPath);
            return chain;
        }

        var folderFinal = rootFinal + "\\" + _folderName;
        problem = CheckDirectory(folder, _folderPath) ?? CheckFinalPath(folder, folderFinal, _folderPath);
        if (problem is not null)
        {
            chain.Result = Refused(problem);
            return chain;
        }

        status = OpenRelative(folder, _fileName,
            forWriting ? FileGenericRead | FileGenericWrite : FileGenericRead,
            forWriting ? NativeMethods.FILE_SHARE_READ : NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
            forWriting ? NativeMethods.FILE_OPEN_IF : NativeMethods.FILE_OPEN, NativeMethods.FILE_NON_DIRECTORY_FILE, out var file);
        if (status != 0)
        {
            file.Dispose();
            chain.Result = OpenFailed(status, _filePath);
            return chain;
        }

        problem = CheckFile(file) ?? CheckFinalPath(file, folderFinal + "\\" + _fileName, _filePath);
        if (problem is not null)
        {
            file.Dispose();
            chain.Result = Refused(problem);
            return chain;
        }

        chain.File = file;
        return chain;
    }

    private static VerifiedRead Refused(string why) => new(VerifiedReadStatus.Refused, Error: why);

    private static VerifiedRead Failed(string why) => new(VerifiedReadStatus.Failed, Error: why);

    private static VerifiedRead OpenFailed(int status, string path)
    {
        if (status is NativeMethods.STATUS_OBJECT_NAME_NOT_FOUND or NativeMethods.STATUS_OBJECT_PATH_NOT_FOUND)
        {
            return new VerifiedRead(VerifiedReadStatus.Missing);
        }

        var win32 = (int)NativeMethods.RtlNtStatusToDosError(status);
        return Failed($"{path} could not be opened: {new Win32Exception(win32).Message} (0x{status:X8})");
    }

    private static string? CheckDirectory(SafeFileHandle handle, string path)
    {
        if (!NativeMethods.GetFileInformationByHandle(handle, out var info))
        {
            return $"the attributes of {path} could not be read.";
        }

        if ((info.FileAttributes & NativeMethods.FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        {
            return $"{path} is a junction, symbolic link or other reparse point.";
        }

        return (info.FileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) == 0 ? $"{path} is not a folder." : null;
    }

    private static string? CheckFile(SafeFileHandle handle)
    {
        if (!NativeMethods.GetFileInformationByHandle(handle, out var info))
        {
            return "the attributes of the settings file could not be read.";
        }

        if ((info.FileAttributes & NativeMethods.FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        {
            return "the settings file is a symbolic link or other reparse point.";
        }

        if ((info.FileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0)
        {
            return "the settings file is a folder.";
        }

        return info.NumberOfLinks == 1
            ? null
            : $"the settings file has {info.NumberOfLinks} hard links, so writing it would also change another file.";
    }

    private static string? CheckFinalPath(SafeFileHandle handle, string expected, string shown)
    {
        var final = FinalPath(handle);
        if (final is null)
        {
            return $"the real location of {shown} could not be determined.";
        }

        return string.Equals(final, expected.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{shown} leads to {final}, not to where it should be.";
    }

    /// <summary>The handle's normalized DOS path without the \\?\ prefix (\\?\UNC\ becomes \\), or null.</summary>
    internal static string? FinalPath(SafeFileHandle handle)
    {
        var buffer = new char[512];
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var length = NativeMethods.GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);
            if (length == 0)
            {
                return null;
            }

            if (length < buffer.Length)
            {
                var path = new string(buffer, 0, (int)length);
                if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                {
                    return @"\\" + path[8..];
                }

                return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
            }

            buffer = new char[length + 1];
        }

        return null;
    }

    /// <summary>The path with 8.3 short names expanded (the TEMP folder of a service account often has them).</summary>
    private static string ExpectedLongPath(string path)
    {
        var buffer = new char[512];
        var length = NativeMethods.GetLongPathName(path, buffer, (uint)buffer.Length);
        if (length > buffer.Length)
        {
            buffer = new char[length];
            length = NativeMethods.GetLongPathName(path, buffer, length);
        }

        return length == 0 || length > buffer.Length ? path.TrimEnd('\\') : new string(buffer, 0, (int)length).TrimEnd('\\');
    }

    /// <summary>NtCreateFile relative to <paramref name="parent"/>, never following a reparse point at <paramref name="name"/>.</summary>
    private static int OpenRelative(SafeFileHandle parent, string name, uint access, uint share, uint disposition, uint kindOption,
        out SafeFileHandle handle)
    {
        var nameBuffer = Marshal.StringToHGlobalUni(name);
        var unicodeString = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.UNICODE_STRING>());
        var addedRef = false;
        try
        {
            Marshal.StructureToPtr(new NativeMethods.UNICODE_STRING
            {
                Length = (ushort)(name.Length * 2),
                MaximumLength = (ushort)(name.Length * 2),
                Buffer = nameBuffer
            }, unicodeString, fDeleteOld: false);

            parent.DangerousAddRef(ref addedRef);
            var attributes = new NativeMethods.OBJECT_ATTRIBUTES
            {
                Length = Marshal.SizeOf<NativeMethods.OBJECT_ATTRIBUTES>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = unicodeString,
                Attributes = NativeMethods.OBJ_CASE_INSENSITIVE
            };

            return NativeMethods.NtCreateFile(out handle, access | NativeMethods.SYNCHRONIZE, ref attributes, out _, IntPtr.Zero,
                NativeMethods.FILE_ATTRIBUTE_NORMAL, share, disposition,
                kindOption | NativeMethods.FILE_OPEN_REPARSE_POINT | NativeMethods.FILE_SYNCHRONOUS_IO_NONALERT, IntPtr.Zero, 0);
        }
        finally
        {
            if (addedRef)
            {
                parent.DangerousRelease();
            }

            Marshal.FreeHGlobal(unicodeString);
            Marshal.FreeHGlobal(nameBuffer);
        }
    }

    /// <summary>The open handles of one operation; disposes all of them.</summary>
    private sealed class Chain : IDisposable
    {
        internal SafeFileHandle? Root { get; set; }

        internal SafeFileHandle? Folder { get; set; }

        internal SafeFileHandle? File { get; set; }

        internal VerifiedRead Result { get; set; } = new(VerifiedReadStatus.Failed, Error: "not opened.");

        public void Dispose()
        {
            File?.Dispose();
            Folder?.Dispose();
            Root?.Dispose();
        }
    }
}
