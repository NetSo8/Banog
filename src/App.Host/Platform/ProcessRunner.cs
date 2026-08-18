using System.Diagnostics;
using Banog.Core.Abstractions;
using Banog.Core.Localization;

namespace Banog.Host.Platform;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        bool waitForExit,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? string.Empty,

            // UseShellExecute=false : pas de passage par ShellExecute, donc pas
            // d'interprétation d'associations de fichiers ni de verbes shell.
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // ArgumentList : chaque argument est échappé par le runtime et transmis tel quel.
        // Aucune ligne de commande n'est construite par concaténation, donc rien à injecter.
        for (var i = 0; i < arguments.Count; i++)
        {
            startInfo.ArgumentList.Add(arguments[i]);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(CoreTexts.CannotStart(executable));

        if (!waitForExit) return 0;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(CoreTexts.CommandTimedOut(timeout.TotalSeconds, executable));
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
