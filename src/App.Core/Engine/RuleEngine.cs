using Banog.Core.Abstractions;
using Banog.Core.Evaluation;
using Banog.Core.Execution;
using Banog.Core.Model;

namespace Banog.Core.Engine;

public sealed record ActionResult(string ActionType, ActionStatus Status, string Message);

public sealed record RuleResult(
    string RuleId,
    string RuleName,
    bool Matched,
    IReadOnlyList<ActionResult> Actions,
    string? Error = null);

public sealed record FileProcessingReport(
    string OriginalPath,
    string FinalPath,
    IReadOnlyList<RuleResult> Rules)
{
    public bool AnyRuleMatched
    {
        get
        {
            for (var i = 0; i < Rules.Count; i++)
            {
                if (Rules[i].Matched) return true;
            }

            return false;
        }
    }
}

/// <summary>
/// Moteur de règles. Aucune dépendance à l'UI ni au watcher : il reçoit un
/// <see cref="FileContext"/> et une liste de règles, il applique. Testable seul.
/// </summary>
public sealed class RuleEngine
{
    private readonly ConditionDispatcher _conditions;
    private readonly Dictionary<Type, IActionHandler> _actions = [];
    private readonly ISystemClock _clock;
    private readonly IRuleLog _log;

    public RuleEngine(
        ConditionDispatcher conditions,
        IEnumerable<IActionHandler> actionHandlers,
        ISystemClock? clock = null,
        IRuleLog? log = null)
    {
        _conditions = conditions;
        _clock = clock ?? SystemClock.Instance;
        _log = log ?? NullRuleLog.Instance;

        foreach (var handler in actionHandlers)
        {
            _actions[handler.ActionType] = handler;
        }
    }

    /// <summary>Compose le moteur v1 à partir des seules dépendances système.</summary>
    public static RuleEngine CreateDefault(
        IFileSystem fileSystem,
        IProcessRunner processRunner,
        ISystemClock? clock = null,
        IRuleLog? log = null)
    {
        clock ??= SystemClock.Instance;
        return new RuleEngine(
            ConditionDispatcher.CreateDefault(clock),
            [
                new MoveActionHandler(fileSystem),
                new CopyActionHandler(fileSystem),
                new RenameActionHandler(fileSystem),
                new DeleteActionHandler(fileSystem),
                new RunCommandActionHandler(processRunner),
            ],
            clock,
            log);
    }

    /// <summary>Évalue les conditions d'une règle sans exécuter ses actions (aperçu / test).</summary>
    public async ValueTask<bool> MatchesAsync(Rule rule, FileContext file, CancellationToken ct = default)
    {
        if (!rule.Enabled) return false;

        var conditions = rule.Conditions;
        if (conditions.Count == 0) return false; // une règle sans condition ne matche rien : garde-fou.

        var all = rule.Match == ConditionMatchMode.All;

        for (var i = 0; i < conditions.Count; i++)
        {
            var result = await _conditions.EvaluateAsync(conditions[i], file, ct).ConfigureAwait(false);

            // En ET, le premier faux tranche ; en OU, le premier vrai. Même test des deux côtés.
            if (all != result) return result;
        }

        return all;
    }

    public async Task<FileProcessingReport> ProcessAsync(
        FileContext file,
        IReadOnlyList<Rule> rules,
        CancellationToken ct = default)
    {
        var context = new ActionExecutionContext(file, _clock);
        var ordered = EnsureOrdered(rules);
        var results = new List<RuleResult>(ordered.Count);

        for (var index = 0; index < ordered.Count; index++)
        {
            var rule = ordered[index];
            if (!rule.Enabled) continue;

            ct.ThrowIfCancellationRequested();

            bool matched;
            try
            {
                matched = await MatchesAsync(rule, context.Current, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error(Localization.CoreTexts.RuleEvaluationFailed(rule.Name), ex);
                results.Add(new RuleResult(rule.Id, rule.Name, false, [], ex.Message));
                continue;
            }

            if (!matched)
            {
                results.Add(new RuleResult(rule.Id, rule.Name, false, []));
                continue;
            }

            var actionResults = await RunActionsAsync(rule, context, ct).ConfigureAwait(false);
            results.Add(new RuleResult(rule.Id, rule.Name, true, actionResults));

            if (rule.StopProcessingOnMatch || !context.FileAvailable) break;
        }

        return new FileProcessingReport(file.FullPath, context.Current.FullPath, results);
    }

    /// <summary>
    /// Trie par ordre d'évaluation, mais seulement si nécessaire. Le contrôleur fournit
    /// déjà des règles triées ; vérifier en O(n) évite un tri et une copie de tableau par
    /// fichier traité, ce qui compte quand un dossier en déverse des dizaines de milliers.
    /// </summary>
    private static IReadOnlyList<Rule> EnsureOrdered(IReadOnlyList<Rule> rules)
    {
        for (var i = 1; i < rules.Count; i++)
        {
            if (rules[i - 1].Order <= rules[i].Order) continue;

            var sorted = rules.ToArray();
            Array.Sort(sorted, static (a, b) => a.Order.CompareTo(b.Order));
            return sorted;
        }

        return rules;
    }

    private async Task<List<ActionResult>> RunActionsAsync(
        Rule rule, ActionExecutionContext context, CancellationToken ct)
    {
        var results = new List<ActionResult>(rule.Actions.Count);

        foreach (var action in rule.Actions)
        {
            ct.ThrowIfCancellationRequested();

            if (!_actions.TryGetValue(action.GetType(), out var handler))
            {
                results.Add(new ActionResult(action.Type, ActionStatus.Failed, "Action non prise en charge."));
                continue;
            }

            try
            {
                var outcome = await handler.ExecuteAsync(action, context, ct).ConfigureAwait(false);
                results.Add(new ActionResult(action.Type, outcome.Status, outcome.Message));

                if (outcome.Status == ActionStatus.Failed)
                {
                    // Une action ratée arrête la règle : enchaîner sur un état incertain
                    // ferait plus de dégâts que de s'arrêter.
                    _log.Warn(Localization.CoreTexts.RuleStopped(rule.Name, outcome.Message));
                    break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error(Localization.CoreTexts.RuleActionFailed(rule.Name, action.Type), ex);
                results.Add(new ActionResult(action.Type, ActionStatus.Failed, ex.Message));
                break;
            }
        }

        return results;
    }
}
