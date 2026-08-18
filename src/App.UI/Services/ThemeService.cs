using Avalonia;
using Avalonia.Styling;
using Banog.Core.Model;

namespace Banog.UI.Services;

public interface IThemeService
{
    ThemePreference Current { get; }
    void Apply(ThemePreference preference);
}

/// <summary>
/// Applique la préférence d'apparence à l'application.
///
/// <see cref="ThemePreference.System"/> se traduit par <see cref="ThemeVariant.Default"/> :
/// Avalonia interroge alors le paramètre clair/sombre de Windows et se repeint tout seul
/// quand l'utilisateur le change, sans que l'application ait à écouter quoi que ce soit.
/// </summary>
public sealed class ThemeService : IThemeService
{
    public ThemePreference Current { get; private set; } = ThemePreference.System;

    public void Apply(ThemePreference preference)
    {
        Current = preference;

        if (Application.Current is not { } application) return;

        application.RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}

/// <summary>
/// Option d'apparence présentée dans l'UI. Le libellé est lu à chaque affichage : les
/// instances sont partagées et servent de sélection, elles ne peuvent pas porter un
/// texte figé dans une langue.
/// </summary>
public sealed record ThemeOption(ThemePreference Value, string LabelKey)
{
    public string Label => Localization.Loc.T(LabelKey);

    public static readonly ThemeOption[] All =
    [
        new(ThemePreference.System, "Theme_System"),
        new(ThemePreference.Light, "Theme_Light"),
        new(ThemePreference.Dark, "Theme_Dark"),
    ];

    public static ThemeOption For(ThemePreference preference) =>
        All.FirstOrDefault(o => o.Value == preference) ?? All[0];
}

/// <summary>Option de langue, sur le même modèle que <see cref="ThemeOption"/>.</summary>
public sealed record LanguageOption(LanguagePreference Value, string LabelKey)
{
    public string Label => Localization.Loc.T(LabelKey);

    public static readonly LanguageOption[] All =
    [
        new(LanguagePreference.System, "Lang_System"),
        new(LanguagePreference.French, "Lang_French"),
        new(LanguagePreference.English, "Lang_English"),
    ];

    public static LanguageOption For(LanguagePreference preference) =>
        All.FirstOrDefault(o => o.Value == preference) ?? All[0];
}

internal sealed class NullThemeService : IThemeService
{
    public ThemePreference Current => ThemePreference.System;
    public void Apply(ThemePreference preference) { }
}
