using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Banog.Core.Model;

namespace Banog.UI.Localization;

/// <summary>Langue réellement affichée, une fois la préférence résolue.</summary>
public enum UiLanguage
{
    French = 0,
    English = 1,
}

/// <summary>
/// Langue de l'interface.
///
/// Les textes vivent dans <see cref="Texts"/> et sont publiés de deux façons depuis la
/// même table : en ressources d'application, pour que le XAML les lise par
/// <c>{DynamicResource}</c>, et par <see cref="T"/> pour le C#. Une traduction n'existe
/// donc qu'à un seul endroit.
///
/// La préférence « Windows » n'est pas un troisième jeu de textes : elle se résout à
/// l'ouverture d'après la langue du système, français si Windows est en français,
/// anglais sinon.
/// </summary>
public static class Loc
{
    private static Dictionary<string, string> _active = Build(UiLanguage.French);
    private static ResourceDictionary? _published;

    /// <summary>Prévenu quand la langue change : les vues se reconstruisent, les résumés se réécrivent.</summary>
    public static event Action? Changed;

    public static UiLanguage Current { get; private set; } = UiLanguage.French;

    public static LanguagePreference Preference { get; private set; } = LanguagePreference.System;

    /// <summary>Le texte d'une clé. Une clé inconnue se rend elle-même : visible, jamais fatal.</summary>
    public static string T(string key) => _active.TryGetValue(key, out var value) ? value : key;

    /// <summary>Texte à trous : « {0} dossiers sous surveillance ».</summary>
    public static string F(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, T(key), arguments);

    /// <summary>
    /// Comment connaître la langue du système. L'hôte le remplace par une interrogation
    /// de Windows : compilée en globalisation invariante, l'application ne peut pas la
    /// déduire de sa propre culture.
    /// </summary>
    public static Func<UiLanguage> SystemLanguageProvider { get; set; } = () =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("fr", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.French
            : UiLanguage.English;

    /// <summary>La langue de Windows, ramenée aux deux langues que Banog parle.</summary>
    public static UiLanguage SystemLanguage => SystemLanguageProvider();

    public static UiLanguage Resolve(LanguagePreference preference) => preference switch
    {
        LanguagePreference.French => UiLanguage.French,
        LanguagePreference.English => UiLanguage.English,
        _ => SystemLanguage,
    };

    /// <summary>
    /// Applique une préférence. Sans changement effectif de langue, rien n'est notifié :
    /// passer de « Windows » à « Français » sur un Windows français ne doit pas
    /// reconstruire la fenêtre.
    /// </summary>
    public static void Apply(LanguagePreference preference)
    {
        var language = Resolve(preference);
        var changed = language != Current || _published is null;

        Preference = preference;
        Current = language;

        if (!changed) return;

        _active = Build(language);

        // Le noyau écrit dans le même journal que l'interface : ses messages suivent.
        Core.Localization.CoreTexts.French = language == UiLanguage.French;

        Publish();
        Changed?.Invoke();
    }

    /// <summary>
    /// Expose les textes en ressources d'application. Le dictionnaire est remplacé d'un
    /// bloc : Avalonia repropage alors la ressource à toutes les liaisons dynamiques.
    /// </summary>
    private static void Publish()
    {
        if (Application.Current is not { } application) return;

        var dictionary = new ResourceDictionary();
        foreach (var (key, value) in _active) dictionary.Add(key, value);

        if (_published is not null) application.Resources.MergedDictionaries.Remove(_published);

        application.Resources.MergedDictionaries.Add(dictionary);
        _published = dictionary;
    }

    private static Dictionary<string, string> Build(UiLanguage language)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, fr, en) in Texts.All)
        {
            table[key] = language == UiLanguage.French ? fr : en;
        }

        return table;
    }
}
