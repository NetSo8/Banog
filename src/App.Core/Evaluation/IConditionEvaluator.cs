using Banog.Core.Abstractions;
using Banog.Core.Model;

namespace Banog.Core.Evaluation;

/// <summary>
/// Évaluateur d'un type de condition. L'API est asynchrone dès la v1 alors que toutes
/// les conditions actuelles sont synchrones : c'est ce qui permettra d'ajouter plus tard
/// des conditions coûteuses (lecture de contenu, OCR, appel LLM local) sans changer la
/// signature ni réécrire le moteur.
/// </summary>
public interface IConditionEvaluator
{
    Type ConditionType { get; }

    ValueTask<bool> EvaluateAsync(
        RuleCondition condition,
        FileContext file,
        IConditionDispatcher dispatcher,
        CancellationToken cancellationToken);
}

/// <summary>Permet à un évaluateur composite (groupe ET/OU) de réévaluer ses enfants.</summary>
public interface IConditionDispatcher
{
    ValueTask<bool> EvaluateAsync(RuleCondition condition, FileContext file, CancellationToken cancellationToken);
}

public abstract class ConditionEvaluator<T> : IConditionEvaluator where T : RuleCondition
{
    public Type ConditionType => typeof(T);

    public ValueTask<bool> EvaluateAsync(
        RuleCondition condition,
        FileContext file,
        IConditionDispatcher dispatcher,
        CancellationToken cancellationToken)
        => EvaluateCoreAsync((T)condition, file, dispatcher, cancellationToken);

    protected abstract ValueTask<bool> EvaluateCoreAsync(
        T condition,
        FileContext file,
        IConditionDispatcher dispatcher,
        CancellationToken cancellationToken);
}
