using System.Text.RegularExpressions;
using Banog.Core.Abstractions;
using Banog.Core.Model;

namespace Banog.Core.Evaluation;

public sealed class ConditionGroupEvaluator : ConditionEvaluator<ConditionGroup>
{
    protected override async ValueTask<bool> EvaluateCoreAsync(
        ConditionGroup condition, FileContext file, IConditionDispatcher dispatcher, CancellationToken ct)
    {
        var children = condition.Children;
        if (children.Count == 0)
        {
            // Un groupe vide ne filtre rien : vrai en ET, faux en OU.
            return condition.Mode == ConditionMatchMode.All;
        }

        var all = condition.Mode == ConditionMatchMode.All;

        // Indexation directe plutôt que foreach : évite l'énumérateur de List<T> par groupe
        // et par fichier, et permet la sortie anticipée sur le premier verdict décisif.
        for (var i = 0; i < children.Count; i++)
        {
            var result = await dispatcher.EvaluateAsync(children[i], file, ct).ConfigureAwait(false);
            if (all != result) return result;
        }

        return all;
    }
}

public sealed class ExtensionConditionEvaluator : ConditionEvaluator<ExtensionCondition>
{
    protected override ValueTask<bool> EvaluateCoreAsync(
        ExtensionCondition condition, FileContext file, IConditionDispatcher dispatcher, CancellationToken ct)
    {
        var extension = file.Extension;
        var extensions = condition.Extensions;
        var contained = false;

        // Comparaison sur spans : ni normalisation allouée côté règle, ni mise en minuscules
        // côté fichier. Balayage linéaire assumé — une liste d'extensions compte typiquement
        // moins de dix entrées, où le parcours bat une table de hachage (pas de calcul de
        // hachage insensible à la casse, pas d'indirection, tout tient en cache).
        for (var i = 0; i < extensions.Count; i++)
        {
            var candidate = extensions[i].AsSpan().Trim().TrimStart(".*");
            if (candidate.Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                contained = true;
                break;
            }
        }

        var result = condition.Match == ExtensionMatch.IsOneOf ? contained : !contained;
        return ValueTask.FromResult(result);
    }
}

public sealed class NameConditionEvaluator : ConditionEvaluator<NameCondition>
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Un <see cref="Regex"/> par condition, construit une fois puis réutilisé.
    /// Les surcharges statiques de <see cref="Regex"/> repassent par un cache global à
    /// chaque appel — hachage du motif compris — ce qui se paie sur chaque fichier.
    /// La table à clés faibles laisse l'entrée disparaître avec la condition, sans fuite
    /// au rechargement de la configuration.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NameCondition, Regex> RegexCache = [];

    private static Regex? GetRegex(NameCondition condition)
    {
        if (RegexCache.TryGetValue(condition, out var cached)) return cached;

        try
        {
            var options = RegexOptions.CultureInvariant;
            if (!condition.CaseSensitive) options |= RegexOptions.IgnoreCase;

            // Regex interprété : le mode compilé génère de l'IL, indisponible sous Native AOT.
            var regex = new Regex(condition.Value, options, RegexTimeout);
            RegexCache.AddOrUpdate(condition, regex);
            return regex;
        }
        catch (ArgumentException)
        {
            // Motif invalide : la condition ne matche pas plutôt que de casser la règle.
            return null;
        }
    }

    protected override ValueTask<bool> EvaluateCoreAsync(
        NameCondition condition, FileContext file, IConditionDispatcher dispatcher, CancellationToken ct)
    {
        var subject = condition.Target == NameTarget.BaseName ? file.BaseName : file.FileName;
        var comparison = condition.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var value = condition.Value.AsSpan();

        bool result;
        if (condition.Mode == TextMatchMode.Regex)
        {
            var regex = GetRegex(condition);

            try
            {
                // Le délai maximal borne le coût d'un motif à explosion combinatoire, qu'il
                // vienne d'une règle maladroite ou d'un nom de fichier construit pour ça.
                result = regex is not null && regex.IsMatch(subject);
            }
            catch (RegexMatchTimeoutException)
            {
                result = false;
            }
        }
        else
        {
            result = condition.Mode switch
            {
                TextMatchMode.Contains => subject.Contains(value, comparison),
                TextMatchMode.StartsWith => subject.StartsWith(value, comparison),
                TextMatchMode.EndsWith => subject.EndsWith(value, comparison),
                TextMatchMode.Equals => subject.Equals(value, comparison),
                _ => false,
            };
        }

        return ValueTask.FromResult(result);
    }
}

public sealed class DateConditionEvaluator(ISystemClock clock) : ConditionEvaluator<DateCondition>
{
    protected override ValueTask<bool> EvaluateCoreAsync(
        DateCondition condition, FileContext file, IConditionDispatcher dispatcher, CancellationToken ct)
    {
        var value = condition.Field == DateField.Created ? file.CreatedUtc : file.ModifiedUtc;

        bool result;
        switch (condition.Comparison)
        {
            case DateComparison.Before:
                result = condition.Instant is { } beforeInstant && value < beforeInstant;
                break;
            case DateComparison.After:
                result = condition.Instant is { } afterInstant && value > afterInstant;
                break;
            default:
            {
                var age = clock.UtcNow - value;
                var threshold = ToTimeSpan(condition.Amount, condition.Unit);
                result = condition.Comparison == DateComparison.OlderThan ? age > threshold : age < threshold;
                break;
            }
        }

        return ValueTask.FromResult(result);
    }

    internal static TimeSpan ToTimeSpan(double amount, TimeUnit unit) => unit switch
    {
        TimeUnit.Minutes => TimeSpan.FromMinutes(amount),
        TimeUnit.Hours => TimeSpan.FromHours(amount),
        TimeUnit.Days => TimeSpan.FromDays(amount),
        TimeUnit.Weeks => TimeSpan.FromDays(amount * 7),
        _ => TimeSpan.Zero,
    };
}

public sealed class SizeConditionEvaluator : ConditionEvaluator<SizeCondition>
{
    protected override ValueTask<bool> EvaluateCoreAsync(
        SizeCondition condition, FileContext file, IConditionDispatcher dispatcher, CancellationToken ct)
    {
        var threshold = condition.ToBytes();
        var result = condition.Comparison switch
        {
            NumericComparison.GreaterThan => file.SizeBytes > threshold,
            NumericComparison.LessThan => file.SizeBytes < threshold,
            NumericComparison.EqualTo => file.SizeBytes == threshold,
            _ => false,
        };

        return ValueTask.FromResult(result);
    }
}

public sealed class SourceFolderConditionEvaluator : ConditionEvaluator<SourceFolderCondition>
{
    protected override ValueTask<bool> EvaluateCoreAsync(
        SourceFolderCondition condition, FileContext file, IConditionDispatcher dispatcher, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(condition.Path))
        {
            return ValueTask.FromResult(false);
        }

        // NormalizeDirectory rend l'instance d'origine quand le chemin est déjà propre :
        // le cas courant ne coûte rien, et la comparaison se fait sur spans.
        var expected = PathUtilities.NormalizeDirectory(condition.Path).AsSpan();
        var actual = file.DirectoryPath;

        var result = condition.IncludeSubfolders
            ? PathUtilities.IsSameOrUnder(actual, expected)
            : actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

        return ValueTask.FromResult(result);
    }
}
