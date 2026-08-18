using Banog.Core.Abstractions;
using Banog.Core.Model;

namespace Banog.Core.Execution;

public enum ActionStatus
{
    Applied = 0,
    Skipped = 1,
    Failed = 2,
}

public readonly record struct ActionOutcome(ActionStatus Status, string Message, string? NewPath = null)
{
    public static ActionOutcome Applied(string message, string? newPath = null) => new(ActionStatus.Applied, message, newPath);
    public static ActionOutcome Skipped(string message) => new(ActionStatus.Skipped, message);
    public static ActionOutcome Failed(string message) => new(ActionStatus.Failed, message);
}

/// <summary>
/// État partagé entre les actions d'une même règle : un déplacement puis un renommage
/// doivent chaîner sur le chemin courant, pas sur le chemin d'origine.
/// </summary>
public sealed class ActionExecutionContext(FileContext file, ISystemClock clock)
{
    public FileContext Current { get; private set; } = file;
    public FileContext Original { get; } = file;
    public ISystemClock Clock { get; } = clock;

    /// <summary>Faux dès qu'une action a supprimé ou déplacé le fichier hors de portée.</summary>
    public bool FileAvailable { get; private set; } = true;

    public void UpdatePath(string newPath) => Current = Current.WithPath(newPath);

    public void MarkConsumed() => FileAvailable = false;
}

public interface IActionHandler
{
    Type ActionType { get; }

    ValueTask<ActionOutcome> ExecuteAsync(
        RuleAction action,
        ActionExecutionContext context,
        CancellationToken cancellationToken);
}

public abstract class ActionHandler<T> : IActionHandler where T : RuleAction
{
    public Type ActionType => typeof(T);

    public ValueTask<ActionOutcome> ExecuteAsync(RuleAction action, ActionExecutionContext context, CancellationToken ct)
        => ExecuteCoreAsync((T)action, context, ct);

    protected abstract ValueTask<ActionOutcome> ExecuteCoreAsync(
        T action, ActionExecutionContext context, CancellationToken cancellationToken);
}
