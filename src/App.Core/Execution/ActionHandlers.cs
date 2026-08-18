using Banog.Core.Abstractions;
using Banog.Core.Evaluation;
using Banog.Core.Internal;
using Banog.Core.Model;

namespace Banog.Core.Execution;

/// <summary>Résolution de collision commune à move / copy / rename.</summary>
internal static class DestinationResolver
{
    private const int MaxAttempts = 10_000;

    /// <summary>
    /// Renvoie le chemin cible libre, ou <c>null</c> si la politique impose de sauter.
    /// <paramref name="overwrite"/> indique si l'appelant doit écraser la cible existante.
    /// </summary>
    internal static string? Resolve(
        IFileSystem fs,
        string directory,
        string fileNameTemplate,
        FileContext file,
        DateTimeOffset now,
        ConflictPolicy policy,
        out bool overwrite,
        bool preserveSourceExtension = false)
    {
        overwrite = false;
        var useCounterToken = TokenExpander.ContainsCounter(fileNameTemplate);

        string ExpandName(int counter) => preserveSourceExtension
            ? TokenExpander.ExpandFileNameWithOriginalExtension(fileNameTemplate, file, now, counter)
            : TokenExpander.ExpandFileName(fileNameTemplate, file, now, counter);

        var firstName = ExpandName(counter: 1);
        var firstPath = Path.Join(directory, firstName);

        if (!fs.FileExists(firstPath)) return firstPath;

        switch (policy)
        {
            case ConflictPolicy.Overwrite:
                overwrite = true;
                return firstPath;

            case ConflictPolicy.Skip:
                return null;

            default:
            {
                // Si le gabarit porte déjà un {counter}, on l'incrémente ; sinon on
                // suffixe « (n) » à la Windows.
                var nameSpan = firstName.AsSpan();
                var dot = nameSpan.LastIndexOf('.');
                var baseName = dot > 0 ? nameSpan[..dot] : nameSpan;
                var extension = dot > 0 ? nameSpan[dot..] : ReadOnlySpan<char>.Empty;

                Span<char> stack = stackalloc char[320];

                for (var n = 2; n <= MaxAttempts; n++)
                {
                    string candidate;

                    if (useCounterToken)
                    {
                        candidate = Path.Join(directory, ExpandName(n));
                    }
                    else
                    {
                        var builder = new ValueStringBuilder(stack);
                        try
                        {
                            builder.Append(baseName);
                            builder.Append(" (");
                            AppendInt(ref builder, n - 1);
                            builder.Append(')');
                            builder.Append(extension);
                            candidate = Path.Join(directory, builder.AsSpan());
                        }
                        finally
                        {
                            builder.Dispose();
                        }
                    }

                    if (!fs.FileExists(candidate)) return candidate;
                }

                return null;
            }
        }
    }

    private static void AppendInt(ref ValueStringBuilder builder, int value)
    {
        var destination = builder.AppendSpan(12);
        value.TryFormat(destination, out var written, provider: System.Globalization.CultureInfo.InvariantCulture);
        builder.Rewind(12 - written);
    }

    /// <summary>
    /// Développe un dossier de destination et vérifie qu'il reste un chemin absolu.
    ///
    /// Les valeurs de tokens sont contraintes à un segment unique par
    /// <see cref="TokenScope.Path"/> ; ce contrôle attrape ce qui resterait : un gabarit qui
    /// se développerait en chemin relatif, dépendant du répertoire courant du processus.
    /// </summary>
    internal static bool TryResolveDirectory(
        string template, FileContext file, DateTimeOffset now, out string directory, out string? error)
    {
        directory = PathUtilities.NormalizeDirectory(
            TokenExpander.Expand(template, file, now, counter: 1, TokenScope.Path));

        if (directory.Length == 0)
        {
            error = Localization.CoreTexts.EmptyDestination;
            return false;
        }

        if (!Path.IsPathFullyQualified(directory))
        {
            error = Localization.CoreTexts.RelativeDestination(directory);
            return false;
        }

        error = null;
        return true;
    }
}

public sealed class MoveActionHandler(IFileSystem fs) : ActionHandler<MoveAction>
{
    protected override ValueTask<ActionOutcome> ExecuteCoreAsync(
        MoveAction action, ActionExecutionContext context, CancellationToken ct)
    {
        if (!context.FileAvailable) return ValueTask.FromResult(ActionOutcome.Skipped(Localization.CoreTexts.FileAlreadyConsumed));
        if (string.IsNullOrWhiteSpace(action.Destination))
            return ValueTask.FromResult(ActionOutcome.Failed("Destination vide."));

        var file = context.Current;
        var now = context.Clock.UtcNow;

        if (!DestinationResolver.TryResolveDirectory(action.Destination, file, now, out var directory, out var error))
            return ValueTask.FromResult(ActionOutcome.Failed(error!));

        if (!fs.DirectoryExists(directory))
        {
            if (!action.CreateDirectories)
                return ValueTask.FromResult(ActionOutcome.Failed(Localization.CoreTexts.MissingDirectory(directory)));
            fs.CreateDirectory(directory);
        }

        var target = DestinationResolver.Resolve(
            fs, directory, file.FileName.ToString(), file, now, action.OnConflict, out var overwrite);

        if (target is null)
            return ValueTask.FromResult(ActionOutcome.Skipped(Localization.CoreTexts.TargetAlreadyThere(directory)));

        fs.Move(file.FullPath, target, overwrite);
        context.UpdatePath(target);
        return ValueTask.FromResult(ActionOutcome.Applied(Localization.CoreTexts.Moved(target), target));
    }
}

public sealed class CopyActionHandler(IFileSystem fs) : ActionHandler<CopyAction>
{
    protected override ValueTask<ActionOutcome> ExecuteCoreAsync(
        CopyAction action, ActionExecutionContext context, CancellationToken ct)
    {
        if (!context.FileAvailable) return ValueTask.FromResult(ActionOutcome.Skipped(Localization.CoreTexts.FileAlreadyConsumed));
        if (string.IsNullOrWhiteSpace(action.Destination))
            return ValueTask.FromResult(ActionOutcome.Failed("Destination vide."));

        var file = context.Current;
        var now = context.Clock.UtcNow;

        if (!DestinationResolver.TryResolveDirectory(action.Destination, file, now, out var directory, out var error))
            return ValueTask.FromResult(ActionOutcome.Failed(error!));

        if (!fs.DirectoryExists(directory))
        {
            if (!action.CreateDirectories)
                return ValueTask.FromResult(ActionOutcome.Failed(Localization.CoreTexts.MissingDirectory(directory)));
            fs.CreateDirectory(directory);
        }

        var target = DestinationResolver.Resolve(
            fs, directory, file.FileName.ToString(), file, now, action.OnConflict, out var overwrite);

        if (target is null)
            return ValueTask.FromResult(ActionOutcome.Skipped(Localization.CoreTexts.TargetAlreadyThere(directory)));

        fs.Copy(file.FullPath, target, overwrite);
        // La copie ne change pas le chemin courant : les actions suivantes restent sur l'original.
        return ValueTask.FromResult(ActionOutcome.Applied(Localization.CoreTexts.Copied(target)));
    }
}

public sealed class RenameActionHandler(IFileSystem fs) : ActionHandler<RenameAction>
{
    protected override ValueTask<ActionOutcome> ExecuteCoreAsync(
        RenameAction action, ActionExecutionContext context, CancellationToken ct)
    {
        if (!context.FileAvailable) return ValueTask.FromResult(ActionOutcome.Skipped(Localization.CoreTexts.FileAlreadyConsumed));
        if (string.IsNullOrWhiteSpace(action.Template))
            return ValueTask.FromResult(ActionOutcome.Failed("Gabarit de renommage vide."));

        var file = context.Current;
        var now = context.Clock.UtcNow;
        var directory = file.DirectoryPath.ToString();

        var target = DestinationResolver.Resolve(
            fs, directory, action.Template, file, now, action.OnConflict, out var overwrite,
            preserveSourceExtension: true);

        if (target is null)
            return ValueTask.FromResult(ActionOutcome.Skipped(Localization.CoreTexts.NameTaken));

        if (string.Equals(target, file.FullPath, StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult(ActionOutcome.Skipped(Localization.CoreTexts.NameUnchanged));

        fs.Move(file.FullPath, target, overwrite);
        context.UpdatePath(target);
        return ValueTask.FromResult(ActionOutcome.Applied(Localization.CoreTexts.Renamed(Path.GetFileName(target)), target));
    }
}

public sealed class DeleteActionHandler(IFileSystem fs) : ActionHandler<DeleteAction>
{
    protected override ValueTask<ActionOutcome> ExecuteCoreAsync(
        DeleteAction action, ActionExecutionContext context, CancellationToken ct)
    {
        if (!context.FileAvailable) return ValueTask.FromResult(ActionOutcome.Skipped(Localization.CoreTexts.FileAlreadyConsumed));

        var path = context.Current.FullPath;

        if (action.UseRecycleBin)
        {
            fs.SendToRecycleBin(path);
        }
        else
        {
            fs.Delete(path);
        }

        context.MarkConsumed();
        return ValueTask.FromResult(ActionOutcome.Applied(
            action.UseRecycleBin ? Localization.CoreTexts.Recycled : Localization.CoreTexts.Deleted));
    }
}

public sealed class RecycleActionHandler(IFileSystem fs) : ActionHandler<RecycleAction>
{
    protected override ValueTask<ActionOutcome> ExecuteCoreAsync(
        RecycleAction action, ActionExecutionContext context, CancellationToken ct)
    {
        if (!context.FileAvailable) return ValueTask.FromResult(ActionOutcome.Skipped(Localization.CoreTexts.FileAlreadyConsumed));

        fs.SendToRecycleBin(context.Current.FullPath);
        context.MarkConsumed();
        return ValueTask.FromResult(ActionOutcome.Applied(Localization.CoreTexts.Recycled));
    }
}

public sealed class RunCommandActionHandler(IProcessRunner runner) : ActionHandler<RunCommandAction>
{
    protected override async ValueTask<ActionOutcome> ExecuteCoreAsync(
        RunCommandAction action, ActionExecutionContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(action.Executable))
        {
            return ActionOutcome.Failed(Localization.CoreTexts.NoExecutable);
        }

        var file = context.Current;
        var now = context.Clock.UtcNow;

        // L'exécutable est un chemin, pas une ligne de commande : les tokens y sont
        // contraints à un segment, on ne veut pas qu'un nom de fichier choisisse le binaire.
        var executable = TokenExpander.Expand(action.Executable, file, now, counter: 1, TokenScope.Path);

        // Découpage d'abord, expansion ensuite : le nombre d'arguments est fixé par le
        // gabarit, une valeur de token ne peut pas en ajouter un.
        var arguments = CommandLineTemplate.Expand(action.Arguments, file, now);

        var workingDirectory = string.IsNullOrWhiteSpace(action.WorkingDirectory)
            ? file.DirectoryPath.ToString()
            : TokenExpander.Expand(action.WorkingDirectory, file, now, counter: 1, TokenScope.Path);

        var timeout = TimeSpan.FromSeconds(Math.Clamp(action.TimeoutSeconds, 1, 3600));
        var exitCode = await runner
            .RunAsync(executable, arguments, workingDirectory, action.WaitForExit, timeout, ct)
            .ConfigureAwait(false);

        return exitCode == 0
            ? ActionOutcome.Applied(Localization.CoreTexts.CommandRan(executable))
            : ActionOutcome.Failed(Localization.CoreTexts.CommandFailed(exitCode, executable));
    }
}
