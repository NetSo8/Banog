using Banog.Core.Abstractions;
using Banog.Core.Internal;

namespace Banog.Core.Execution;

/// <summary>
/// Découpe un gabarit d'arguments en arguments distincts, puis développe les tokens
/// <em>à l'intérieur de chaque argument déjà délimité</em>.
///
/// L'ordre compte, et c'est tout l'enjeu : le découpage porte sur le gabarit, qui vient de
/// l'utilisateur ; l'expansion vient après. Une valeur de token ne peut donc jamais créer
/// un argument supplémentaire, quels que soient les espaces, guillemets ou métacaractères
/// qu'elle contient. Un fichier nommé <c>rapport &amp; del /q *.pdf</c> reste un argument
/// unique et littéral.
///
/// Les arguments sont ensuite remis au processus un par un (<c>ArgumentList</c>), jamais
/// concaténés en ligne de commande.
/// </summary>
public static class CommandLineTemplate
{
    /// <summary>Découpe le gabarit en segments, guillemets doubles respectés.</summary>
    public static List<string> Split(string template)
    {
        var segments = new List<string>();
        if (string.IsNullOrWhiteSpace(template)) return segments;

        Span<char> stack = stackalloc char[256];
        var builder = new ValueStringBuilder(stack);

        try
        {
            var quoted = false;
            var started = false;

            foreach (var c in template.AsSpan())
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    started = true;
                    continue;
                }

                if (!quoted && char.IsWhiteSpace(c))
                {
                    if (started)
                    {
                        segments.Add(builder.ToString());
                        builder.Dispose();
                        builder = new ValueStringBuilder(stack);
                        started = false;
                    }

                    continue;
                }

                builder.Append(c);
                started = true;
            }

            if (started) segments.Add(builder.ToString());
        }
        finally
        {
            builder.Dispose();
        }

        return segments;
    }

    /// <summary>Développe chaque segment séparément. Le nombre d'arguments est fixé par le gabarit.</summary>
    public static string[] Expand(string template, FileContext file, DateTimeOffset now)
    {
        var segments = Split(template);
        if (segments.Count == 0) return [];

        var expanded = new string[segments.Count];
        for (var i = 0; i < segments.Count; i++)
        {
            expanded[i] = TokenExpander.Expand(segments[i], file, now, counter: 1, TokenScope.Raw);
        }

        return expanded;
    }
}
