using System.Buffers;
using Banog.Core.Internal;

namespace Banog.Core.Evaluation;

public static class PathUtilities
{
    private static readonly SearchValues<char> InvalidNameChars =
        SearchValues.Create(Path.GetInvalidFileNameChars());

    private static readonly SearchValues<char> Separators = SearchValues.Create(@"\/");

    /// <summary>
    /// Chemin de dossier normalisé : séparateurs uniformes, pas de séparateur final.
    /// Retourne l'instance d'origine quand elle est déjà normalisée — cas de très loin le
    /// plus fréquent, et le seul chemin de code appelé par fichier traité.
    /// </summary>
    public static string NormalizeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var span = path.AsSpan();
        if (!NeedsNormalization(span)) return path;

        var trimmed = span.Trim();
        Span<char> stack = stackalloc char[260];
        using var builder = new ValueStringBuilder(stack);

        foreach (var c in trimmed)
        {
            builder.Append(c == Path.AltDirectorySeparatorChar ? Path.DirectorySeparatorChar : c);
        }

        var result = builder.AsSpan().TrimEnd(Path.DirectorySeparatorChar);

        // « C: » seul redevient « C:\ » pour rester un chemin racine valide.
        return result.Length == 2 && result[1] == ':'
            ? string.Concat(result, SeparatorString.AsSpan())
            : result.ToString();
    }

    private static readonly string SeparatorString = Path.DirectorySeparatorChar.ToString();

    private static bool NeedsNormalization(ReadOnlySpan<char> path)
    {
        if (path.Length == 0) return false;
        if (char.IsWhiteSpace(path[0]) || char.IsWhiteSpace(path[^1])) return true;
        if (path[^1] == Path.DirectorySeparatorChar && path.Length > 3) return true;
        if (path.IndexOf(Path.AltDirectorySeparatorChar) >= 0) return true;
        if (path.Length == 2 && path[1] == ':') return true;
        return false;
    }

    /// <summary>Vrai si <paramref name="candidate"/> est <paramref name="root"/> ou un sous-dossier.</summary>
    public static bool IsSameOrUnder(ReadOnlySpan<char> candidate, ReadOnlySpan<char> root)
    {
        if (root.IsEmpty) return false;

        if (candidate.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;

        // Le séparateur est obligatoire, sinon « C:\Downloads2 » passerait pour un
        // sous-dossier de « C:\Downloads ».
        var withSeparator = root[^1] == Path.DirectorySeparatorChar ? root.Length : root.Length + 1;
        if (candidate.Length < withSeparator) return false;

        if (root[^1] != Path.DirectorySeparatorChar && candidate[root.Length] != Path.DirectorySeparatorChar)
        {
            return false;
        }

        return candidate[..root.Length].Equals(root, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContainsInvalidNameChars(ReadOnlySpan<char> name) =>
        name.IndexOfAny(InvalidNameChars) >= 0;

    /// <summary>Remplace les caractères interdits dans un nom de fichier.</summary>
    public static string SanitizeFileName(string name, char replacement = '_')
    {
        if (!ContainsInvalidNameChars(name)) return name;

        return string.Create(name.Length, (name, replacement), static (span, state) =>
        {
            var (source, repl) = state;
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = InvalidNameChars.Contains(source[i]) ? repl : source[i];
            }
        });
    }

    /// <summary>
    /// Écrit une valeur de token dans un contexte de chemin en la contraignant à un unique
    /// segment : séparateurs, deux-points et jokers sont neutralisés, et « . » / « .. » sont
    /// remplacés.
    ///
    /// C'est la barrière qui empêche le contenu d'un nom de fichier — donnée non maîtrisée,
    /// puisqu'elle vient de ce qui atterrit dans le dossier surveillé — de faire sortir une
    /// destination de l'arborescence prévue par la règle.
    /// </summary>
    internal static void AppendAsSingleSegment(
        ref ValueStringBuilder builder, ReadOnlySpan<char> value, char replacement = '_')
    {
        if (value.IsEmpty) return;

        if (value is "." or "..")
        {
            builder.Append(replacement);
            return;
        }

        if (!ContainsInvalidNameChars(value))
        {
            builder.Append(value);
            return;
        }

        var destination = builder.AppendSpan(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            destination[i] = InvalidNameChars.Contains(value[i]) ? replacement : value[i];
        }
    }

    /// <summary>Vrai si le chemin comporte un séparateur (donc plus d'un segment).</summary>
    internal static bool HasSeparator(ReadOnlySpan<char> value) => value.IndexOfAny(Separators) >= 0;
}
