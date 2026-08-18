using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Banog.Watcher.Native;
using Microsoft.Win32.SafeHandles;

namespace Banog.Watcher;

/// <summary>
/// Surveillance d'un dossier par <c>ReadDirectoryChangesW</c> : notifications poussées par
/// le noyau, aucun polling, aucun parcours périodique de l'arborescence.
///
/// L'appel est fait en mode bloquant sur un thread dédié plutôt qu'en I/O recouvrante :
/// un thread par dossier surveillé reste largement soutenable pour cet usage, et l'annulation
/// propre passe par <c>CancelIoEx</c>. Le cas OVERLAPPED n'apporterait de gain qu'avec des
/// dizaines de dossiers surveillés simultanément.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DirectoryWatcher : IDisposable
{
    private const uint NotifyFilter =
        NativeMethods.FILE_NOTIFY_CHANGE_FILE_NAME |
        NativeMethods.FILE_NOTIFY_CHANGE_DIR_NAME |
        NativeMethods.FILE_NOTIFY_CHANGE_SIZE |
        NativeMethods.FILE_NOTIFY_CHANGE_LAST_WRITE |
        NativeMethods.FILE_NOTIFY_CHANGE_CREATION;

    private readonly string _path;
    private readonly bool _includeSubfolders;
    private readonly int _bufferSize;
    private readonly SafeFileHandle _handle;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();
    private int _disposed;

    /// <summary>Événements bruts. Levé depuis le thread de surveillance.</summary>
    public event Action<FileChangeEvent>? Changed;

    /// <summary>Erreur non récupérable : la surveillance de ce dossier s'est arrêtée.</summary>
    public event Action<string, Exception>? Faulted;

    /// <summary>Le buffer noyau a débordé : des événements ont été perdus, un rescan est requis.</summary>
    public event Action<string>? Overflowed;

    public string Path => _path;

    public DirectoryWatcher(string path, bool includeSubfolders, int bufferSize = 64 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = System.IO.Path.GetFullPath(path);
        _includeSubfolders = includeSubfolders;
        // ReadDirectoryChangesW plafonne à 64 Ko sur les partages réseau.
        _bufferSize = Math.Clamp(bufferSize, 4 * 1024, 64 * 1024);

        _handle = NativeMethods.CreateFile(
            _path,
            NativeMethods.FILE_LIST_DIRECTORY,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
            securityAttributes: 0,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS,
            templateFile: 0);

        if (_handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            _handle.Dispose();
            throw new Win32Exception(error, Banog.Core.Localization.CoreTexts.CannotOpenFolder(_path));
        }

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = $"banog-watch:{System.IO.Path.GetFileName(_path)}",
        };
    }

    public void Start() => _thread.Start();

    private unsafe void Loop()
    {
        // Buffer épinglé : l'adresse est passée au noyau pour toute la durée de l'appel.
        var buffer = GC.AllocateArray<byte>(_bufferSize, pinned: true);

        try
        {
            fixed (byte* pinned = buffer)
            {
                while (!_cts.IsCancellationRequested)
                {
                    uint bytesReturned = 0;

                    var ok = NativeMethods.ReadDirectoryChangesW(
                        _handle,
                        pinned,
                        (uint)buffer.Length,
                        _includeSubfolders,
                        NotifyFilter,
                        &bytesReturned,
                        overlapped: 0,
                        completionRoutine: 0);

                    if (!ok)
                    {
                        var error = Marshal.GetLastPInvokeError();

                        if (_cts.IsCancellationRequested ||
                            error is NativeMethods.ERROR_OPERATION_ABORTED or NativeMethods.ERROR_INVALID_HANDLE)
                        {
                            return;
                        }

                        if (error == NativeMethods.ERROR_NOTIFY_ENUM_DIR)
                        {
                            Overflowed?.Invoke(_path);
                            continue;
                        }

                        Faulted?.Invoke(_path, new Win32Exception(error));
                        return;
                    }

                    if (bytesReturned == 0)
                    {
                        // Débordement du buffer noyau : les changements sont perdus.
                        Overflowed?.Invoke(_path);
                        continue;
                    }

                    Dispatch(new ReadOnlySpan<byte>(pinned, (int)bytesReturned));
                }
            }
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            Faulted?.Invoke(_path, ex);
        }
    }

    /// <summary>Décodage de la chaîne de structures FILE_NOTIFY_INFORMATION.</summary>
    private void Dispatch(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        string? pendingOldName = null;

        while (offset + 12 <= data.Length)
        {
            var entry = data[offset..];

            var nextEntryOffset = BitConverter.ToUInt32(entry[..4]);
            var action = (FileNotifyAction)BitConverter.ToUInt32(entry.Slice(4, 4));
            var nameLengthBytes = (int)BitConverter.ToUInt32(entry.Slice(8, 4));

            if (12 + nameLengthBytes > entry.Length) break;

            var name = MemoryMarshal.Cast<byte, char>(entry.Slice(12, nameLengthBytes)).ToString();
            var fullPath = System.IO.Path.Combine(_path, name);

            switch (action)
            {
                case FileNotifyAction.Added:
                    Changed?.Invoke(new FileChangeEvent(fullPath, _path, FileChangeKind.Created));
                    break;
                case FileNotifyAction.Modified:
                    Changed?.Invoke(new FileChangeEvent(fullPath, _path, FileChangeKind.Changed));
                    break;
                case FileNotifyAction.Removed:
                    Changed?.Invoke(new FileChangeEvent(fullPath, _path, FileChangeKind.Deleted));
                    break;
                case FileNotifyAction.RenamedOldName:
                    pendingOldName = fullPath;
                    break;
                case FileNotifyAction.RenamedNewName:
                    Changed?.Invoke(new FileChangeEvent(fullPath, _path, FileChangeKind.Renamed, pendingOldName));
                    pendingOldName = null;
                    break;
            }

            if (nextEntryOffset == 0) break;
            offset += (int)nextEntryOffset;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _cts.Cancel();

        // Débloque le ReadDirectoryChangesW en cours.
        if (!_handle.IsInvalid) NativeMethods.CancelIoEx(_handle, 0);

        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(2));

        _handle.Dispose();
        _cts.Dispose();
    }
}
