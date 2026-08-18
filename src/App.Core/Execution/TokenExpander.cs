using System.Globalization;
using Banog.Core.Abstractions;
using Banog.Core.Evaluation;
using Banog.Core.Internal;

namespace Banog.Core.Execution;

/// <summary>
/// Contrôle ce qu'une valeur de token a le droit de produire.
/// Le gabarit vient de l'utilisateur, mais les <em>valeurs</em> substituées viennent du
/// fichier traité — c'est-à-dire de ce que quelqu'un a déposé dans le dossier surveillé.
/// </summary>
public enum TokenScope
{
    /// <summary>
    /// Contexte chemin : les valeurs sont contraintes à un seul segment. Les séparateurs
    /// présents littéralement dans le gabarit restent intacts.
    /// </summary>
    Path = 0,

    /// <summary>Contexte nom de fichier : idem, et le résultat entier reste un nom valide.</summary>
    FileName = 1,

    /// <summary>
    /// Aucune contrainte. Réservé aux arguments de commande, où <c>{path}</c> doit rendre
    /// le vrai chemin complet. La protection y est ailleurs : chaque argument est passé
    /// séparément au processus, jamais concaténé dans une ligne de commande.
    /// </summary>
    Raw = 2,
}

/// <summary>
/// Développe les tokens d'un gabarit de nom ou de chemin.
///
/// Tokens v1 :
///   {name}      nom d'origine sans extension
///   {filename}  nom d'origine avec extension
///   {ext}       extension sans le point
///   {path}      chemin complet du fichier
///   {folder}    nom du dossier parent
///   {counter}   compteur de désambiguïsation ({counter:000} pour le zéro-padding)
///   {created:F} date de création, F = format .NET (défaut yyyy-MM-dd)
///   {modified:F} date de modification
///   {now:F}     date courante
/// Échappement : {{ et }} produisent une accolade littérale.
///
/// L'écriture passe par un tampon de pile : un gabarit court ne provoque qu'une seule
/// allocation, la chaîne de résultat.
/// </summary>
public static class TokenExpander
{
    private const string DefaultDateFormat = "yyyy-MM-dd";
    private const int StackBufferSize = 320;

    /// <summary>Vrai si le gabarit contient un token compteur (change la stratégie de conflit).</summary>
    public static bool ContainsCounter(string template)
        => template.Contains("{counter", StringComparison.OrdinalIgnoreCase)
        || template.Contains("{compteur", StringComparison.OrdinalIgnoreCase);

    public static string Expand(
        string template, FileContext file, DateTimeOffset now, int counter = 1, TokenScope scope = TokenScope.Path)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        Span<char> stack = stackalloc char[StackBufferSize];
        var builder = new ValueStringBuilder(stack);

        // Pas de « using » : le tampon est passé par ref, ce qu'un using-variable interdit.
        try
        {
            Expand(ref builder, template, file, now, counter, scope);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>Développe un gabarit destiné à un nom de fichier.</summary>
    public static string ExpandFileName(string template, FileContext file, DateTimeOffset now, int counter = 1)
    {
        var expanded = Expand(template, file, now, counter, TokenScope.FileName);

        // Filet de sécurité : le gabarit lui-même peut contenir des caractères interdits.
        return PathUtilities.SanitizeFileName(expanded);
    }

    internal static void Expand(
        ref ValueStringBuilder builder,
        string template,
        FileContext file,
        DateTimeOffset now,
        int counter,
        TokenScope scope)
    {
        var span = template.AsSpan();
        var i = 0;

        while (i < span.Length)
        {
            var c = span[i];

            if (c == '{')
            {
                if (i + 1 < span.Length && span[i + 1] == '{')
                {
                    builder.Append('{');
                    i += 2;
                    continue;
                }

                var close = span[(i + 1)..].IndexOf('}');
                if (close < 0)
                {
                    // Accolade non fermée : conservée telle quelle plutôt que de jeter.
                    builder.Append(span[i..]);
                    return;
                }

                AppendToken(ref builder, span.Slice(i + 1, close), file, now, counter, scope);
                i += close + 2;
                continue;
            }

            if (c == '}' && i + 1 < span.Length && span[i + 1] == '}')
            {
                builder.Append('}');
                i += 2;
                continue;
            }

            builder.Append(c);
            i++;
        }
    }

    private static void AppendToken(
        ref ValueStringBuilder builder,
        ReadOnlySpan<char> token,
        FileContext file,
        DateTimeOffset now,
        int counter,
        TokenScope scope)
    {
        var colon = token.IndexOf(':');
        var name = colon >= 0 ? token[..colon] : token;
        var format = colon >= 0 ? token[(colon + 1)..] : ReadOnlySpan<char>.Empty;

        // Le switch sur span à constantes littérales se compile en aiguillage par longueur
        // puis par caractère : un seul test dans le cas courant. La chaîne de comparaisons
        // insensibles à la casse qui suit ne sert qu'aux graphies non canoniques.
        // Chaque token a un nom anglais (stable, celui qui est écrit dans les fichiers de
        // règles existants) et un alias français, parce qu'une interface en français qui
        // demande d'écrire « {name} » n'est simple qu'en apparence. Les deux graphies
        // restent valides indéfiniment.
        switch (name)
        {
            case "name" or "nom": AppendValue(ref builder, file.BaseName, scope); return;
            case "filename" or "fichier": AppendValue(ref builder, file.FileName, scope); return;
            case "ext" or "extension": AppendValue(ref builder, file.Extension, scope); return;
            case "folder" or "dossier": AppendValue(ref builder, file.DirectoryName, scope); return;

            // Un chemin complet ne tient pas dans un segment : hors contexte Raw il est
            // aplati, ce qui est le comportement voulu pour un nom de fichier.
            case "path" or "chemin": AppendValue(ref builder, file.FullPath, scope); return;

            case "counter" or "compteur": AppendNumber(ref builder, counter, format); return;

            // « {date} » sans précision désigne la date de création : c'est celle qu'on veut
            // dans la quasi-totalité des cas de classement.
            case "created" or "creation" or "création" or "date":
                AppendDate(ref builder, file.CreatedUtc.ToLocalTime(), format);
                return;

            case "modified" or "modification":
                AppendDate(ref builder, file.ModifiedUtc.ToLocalTime(), format);
                return;

            case "now" or "aujourdhui":
                AppendDate(ref builder, now.ToLocalTime(), format);
                return;
        }

        AppendTokenIgnoringCase(ref builder, token, name, format, file, now, counter, scope);
    }

    /// <summary>Repli pour « {Name} », « {EXT} » et autres graphies acceptées mais rares.</summary>
    private static void AppendTokenIgnoringCase(
        ref ValueStringBuilder builder,
        ReadOnlySpan<char> token,
        ReadOnlySpan<char> name,
        ReadOnlySpan<char> format,
        FileContext file,
        DateTimeOffset now,
        int counter,
        TokenScope scope)
    {
        if (Is(name, "name") || Is(name, "nom")) { AppendValue(ref builder, file.BaseName, scope); return; }
        if (Is(name, "filename") || Is(name, "fichier")) { AppendValue(ref builder, file.FileName, scope); return; }
        if (Is(name, "ext") || Is(name, "extension")) { AppendValue(ref builder, file.Extension, scope); return; }
        if (Is(name, "folder") || Is(name, "dossier")) { AppendValue(ref builder, file.DirectoryName, scope); return; }
        if (Is(name, "path") || Is(name, "chemin")) { AppendValue(ref builder, file.FullPath, scope); return; }
        if (Is(name, "counter") || Is(name, "compteur")) { AppendNumber(ref builder, counter, format); return; }

        if (Is(name, "created") || Is(name, "creation") || Is(name, "création") || Is(name, "date"))
        {
            AppendDate(ref builder, file.CreatedUtc.ToLocalTime(), format);
            return;
        }

        if (Is(name, "modified") || Is(name, "modification"))
        {
            AppendDate(ref builder, file.ModifiedUtc.ToLocalTime(), format);
            return;
        }

        if (Is(name, "now") || Is(name, "aujourdhui")) { AppendDate(ref builder, now.ToLocalTime(), format); return; }

        // Token inconnu : conservé littéralement, plus lisible qu'une disparition silencieuse.
        builder.Append('{');
        builder.Append(token);
        builder.Append('}');
    }

    private static void AppendValue(ref ValueStringBuilder builder, ReadOnlySpan<char> value, TokenScope scope)
    {
        if (scope == TokenScope.Raw) builder.Append(value);
        else PathUtilities.AppendAsSingleSegment(ref builder, value);
    }

    private static void AppendNumber(ref ValueStringBuilder builder, int value, ReadOnlySpan<char> format)
    {
        // TryFormat écrit directement dans le tampon : pas de chaîne intermédiaire.
        var destination = builder.AppendSpan(16);
        if (value.TryFormat(destination, out var written, format, CultureInfo.InvariantCulture))
        {
            builder.Rewind(16 - written);
        }
        else
        {
            builder.Rewind(16);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendDate(ref ValueStringBuilder builder, DateTimeOffset value, ReadOnlySpan<char> format)
    {
        var pattern = format.IsEmpty ? DefaultDateFormat.AsSpan() : format;

        var destination = builder.AppendSpan(64);
        if (value.TryFormat(destination, out var written, pattern, CultureInfo.InvariantCulture))
        {
            builder.Rewind(64 - written);
        }
        else
        {
            builder.Rewind(64);
            builder.Append(value.ToString(pattern.ToString(), CultureInfo.InvariantCulture));
        }
    }

    private static bool Is(ReadOnlySpan<char> token, string expected)
        => token.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
