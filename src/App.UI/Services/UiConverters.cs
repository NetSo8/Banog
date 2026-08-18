using Avalonia.Data.Converters;

namespace Banog.UI.Services;

/// <summary>
/// Convertisseurs exposés en statiques et référencés par <c>{x:Static}</c> : pas
/// d'instanciation par réflexion depuis le XAML, donc rien à annoter pour l'AOT.
/// </summary>
public static class UiConverters
{
    public static readonly IValueConverter RunningLabel =
        new FuncValueConverter<bool, string>(running =>
            Localization.Loc.T(running ? "State_Running" : "State_Paused"));

    public static readonly IValueConverter ToggleLabel =
        new FuncValueConverter<bool, string>(running =>
            Localization.Loc.T(running ? "Side_Pause" : "Side_Start"));

    public static readonly IValueConverter LeafName =
        new FuncValueConverter<string?, string>(path =>
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        });

    public static readonly IValueConverter IsNull =
        new FuncValueConverter<object?, bool>(value => value is null);

    public static readonly IValueConverter IsNotNull =
        new FuncValueConverter<object?, bool>(value => value is not null);

    public static readonly IValueConverter Not =
        new FuncValueConverter<bool, bool>(value => !value);

    /// <summary>Un compteur non nul. Sert à ne colorer un chiffre que s'il y a matière.</summary>
    public static readonly IValueConverter IsPositive =
        new FuncValueConverter<int, bool>(value => value > 0);

}
