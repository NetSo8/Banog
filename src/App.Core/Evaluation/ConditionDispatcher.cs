using Banog.Core.Abstractions;
using Banog.Core.Model;

namespace Banog.Core.Evaluation;

/// <summary>
/// Aiguille chaque condition vers son évaluateur, et applique la négation de façon
/// centralisée pour qu'aucun évaluateur n'ait à s'en soucier.
/// </summary>
public sealed class ConditionDispatcher : IConditionDispatcher
{
    private readonly Dictionary<Type, IConditionEvaluator> _evaluators = [];

    public ConditionDispatcher(IEnumerable<IConditionEvaluator> evaluators)
    {
        foreach (var evaluator in evaluators)
        {
            _evaluators[evaluator.ConditionType] = evaluator;
        }
    }

    /// <summary>Jeu d'évaluateurs de la v1.</summary>
    public static ConditionDispatcher CreateDefault(ISystemClock? clock = null) => new(
    [
        new ConditionGroupEvaluator(),
        new ExtensionConditionEvaluator(),
        new NameConditionEvaluator(),
        new DateConditionEvaluator(clock ?? SystemClock.Instance),
        new SizeConditionEvaluator(),
        new SourceFolderConditionEvaluator(),
    ]);

    /// <summary>Branche un évaluateur supplémentaire (conditions de contenu, OCR, IA...).</summary>
    public ConditionDispatcher Register(IConditionEvaluator evaluator)
    {
        _evaluators[evaluator.ConditionType] = evaluator;
        return this;
    }

    public bool CanEvaluate(RuleCondition condition) => _evaluators.ContainsKey(condition.GetType());

    public async ValueTask<bool> EvaluateAsync(RuleCondition condition, FileContext file, CancellationToken ct)
    {
        if (!_evaluators.TryGetValue(condition.GetType(), out var evaluator))
        {
            throw new InvalidOperationException(
                $"Aucun évaluateur enregistré pour la condition \"{condition.Type}\".");
        }

        var result = await evaluator.EvaluateAsync(condition, file, this, ct).ConfigureAwait(false);
        return condition.Negate ? !result : result;
    }
}
