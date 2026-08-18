using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Banog.Core.Abstractions;
using Banog.Core.Localization;

namespace Banog.Host.Platform;

/// <summary>Implémentation disque réelle des opérations du moteur de règles.</summary>
[SupportedOSPlatform("windows")]
public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void Move(string source, string destination, bool overwrite) =>
        File.Move(source, destination, overwrite);

    public void Copy(string source, string destination, bool overwrite) =>
        File.Copy(source, destination, overwrite);

    public void Delete(string path) => File.Delete(path);

    public void SendToRecycleBin(string path) => RecycleBin.Send(path);

    public FileMetadata? TryGetMetadata(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;

            return new FileMetadata(
                info.Length,
                new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Envoi à la corbeille via <c>SHFileOperationW</c>. La chaîne source doit être
/// terminée par un double NUL : l'API accepte une liste de chemins.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOpStruct
    {
        public nint Hwnd;
        public uint Func;
        public nint From;
        public nint To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public nint NameMappings;
        public nint ProgressTitle;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    private static partial int SHFileOperation(ref ShFileOpStruct fileOp);

    internal static void Send(string path)
    {
        var full = Path.GetFullPath(path);

        // Double terminaison NUL : un caractère de fin ajouté par le marshalling ne suffit pas.
        var from = Marshal.StringToHGlobalUni(full + '\0');

        try
        {
            var operation = new ShFileOpStruct
            {
                Func = FO_DELETE,
                From = from,
                Flags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
            };

            var result = SHFileOperation(ref operation);
            if (result != 0)
            {
                throw new Win32Exception(result, CoreTexts.RecycleFailed(full));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }
}
