using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Banog.Watcher.Native;

/// <summary>
/// P/Invoke via <c>LibraryImport</c> : le marshalling est généré à la compilation,
/// donc pas de génération d'IL à l'exécution — compatible Native AOT.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    internal const uint FILE_LIST_DIRECTORY = 0x0001;

    internal const uint FILE_SHARE_READ = 0x0001;
    internal const uint FILE_SHARE_WRITE = 0x0002;
    internal const uint FILE_SHARE_DELETE = 0x0004;

    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x0200_0000;

    internal const uint FILE_NOTIFY_CHANGE_FILE_NAME = 0x0000_0001;
    internal const uint FILE_NOTIFY_CHANGE_DIR_NAME = 0x0000_0002;
    internal const uint FILE_NOTIFY_CHANGE_ATTRIBUTES = 0x0000_0004;
    internal const uint FILE_NOTIFY_CHANGE_SIZE = 0x0000_0008;
    internal const uint FILE_NOTIFY_CHANGE_LAST_WRITE = 0x0000_0010;
    internal const uint FILE_NOTIFY_CHANGE_CREATION = 0x0000_0040;

    internal const int ERROR_OPERATION_ABORTED = 995;
    internal const int ERROR_INVALID_HANDLE = 6;
    internal const int ERROR_NOTIFY_ENUM_DIR = 1022;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool ReadDirectoryChangesW(
        SafeFileHandle directory,
        void* buffer,
        uint bufferLength,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint notifyFilter,
        uint* bytesReturned,
        nint overlapped,
        nint completionRoutine);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CancelIoEx(SafeFileHandle handle, nint overlapped);
}

/// <summary>Actions rapportées par ReadDirectoryChangesW.</summary>
internal enum FileNotifyAction : uint
{
    Added = 1,
    Removed = 2,
    Modified = 3,
    RenamedOldName = 4,
    RenamedNewName = 5,
}
