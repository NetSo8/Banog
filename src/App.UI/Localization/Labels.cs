using Avalonia.Data.Converters;
using Banog.Core.Model;
using Banog.UI.ViewModels;

namespace Banog.UI.Localization;

/// <summary>
/// Libellés lisibles des énumérations du modèle.
///
/// Les valeurs d'énumération sont un détail d'implémentation : « IsOneOf », « BaseName » ou
/// « Any » n'ont aucun sens pour qui range ses téléchargements. Le modèle et le format JSON
/// gardent leurs noms stables ; seul l'affichage est traduit, et en un seul endroit.
///
/// Chaque valeur pointe vers une clé de <see cref="Texts"/> : la traduction se fait donc
/// là où sont toutes les autres, et suit la langue choisie.
/// </summary>
public static class Labels
{
    /// <summary>Convertisseur unique branché sur tous les sélecteurs (voir la classe de style « enum »).</summary>
    public static readonly IValueConverter Display =
        new FuncValueConverter<object?, string>(value => value is null ? string.Empty : For(value));

    /// <summary>Même libellé, en bouton d'ajout : « ＋ Date de création ».</summary>
    public static readonly IValueConverter AddDisplay =
        new FuncValueConverter<object?, string>(value =>
            value is null ? string.Empty : Loc.F("Part_Add", For(value)));

    public static string For(object value) => Loc.T(KeyFor(value));

    private static string KeyFor(object value) => value switch
    {
        ExtensionMatch v => v switch
        {
            ExtensionMatch.IsOneOf => "Enum_ExtIsOneOf",
            ExtensionMatch.IsNotOneOf => "Enum_ExtIsNotOneOf",
            _ => v.ToString(),
        },

        NameTarget v => v switch
        {
            NameTarget.BaseName => "Enum_TargetBase",
            NameTarget.FullName => "Enum_TargetFull",
            _ => v.ToString(),
        },

        TextMatchMode v => v switch
        {
            TextMatchMode.Contains => "Enum_Contains",
            TextMatchMode.StartsWith => "Enum_StartsWith",
            TextMatchMode.EndsWith => "Enum_EndsWith",
            TextMatchMode.Equals => "Enum_Equals",
            TextMatchMode.Regex => "Enum_Regex",
            _ => v.ToString(),
        },

        DateField v => v switch
        {
            DateField.Created => "Enum_Created",
            DateField.Modified => "Enum_Modified",
            _ => v.ToString(),
        },

        DateComparison v => v switch
        {
            DateComparison.OlderThan => "Enum_OlderThan",
            DateComparison.NewerThan => "Enum_NewerThan",
            DateComparison.Before => "Enum_Before",
            DateComparison.After => "Enum_After",
            _ => v.ToString(),
        },

        TimeUnit v => v switch
        {
            TimeUnit.Minutes => "Enum_Minutes",
            TimeUnit.Hours => "Enum_Hours",
            TimeUnit.Days => "Enum_Days",
            TimeUnit.Weeks => "Enum_Weeks",
            _ => v.ToString(),
        },

        NumericComparison v => v switch
        {
            NumericComparison.GreaterThan => "Enum_GreaterThan",
            NumericComparison.LessThan => "Enum_LessThan",
            NumericComparison.EqualTo => "Enum_EqualTo",
            _ => v.ToString(),
        },

        SizeUnit v => v switch
        {
            SizeUnit.Bytes => "Enum_Bytes",
            SizeUnit.Kilobytes => "Enum_Kilobytes",
            SizeUnit.Megabytes => "Enum_Megabytes",
            SizeUnit.Gigabytes => "Enum_Gigabytes",
            _ => v.ToString(),
        },

        ConflictPolicy v => v switch
        {
            ConflictPolicy.Rename => "Enum_KeepBoth",
            ConflictPolicy.Overwrite => "Enum_Overwrite",
            ConflictPolicy.Skip => "Enum_Skip",
            _ => v.ToString(),
        },

        ConditionMatchMode v => v switch
        {
            ConditionMatchMode.All => "Enum_All",
            ConditionMatchMode.Any => "Enum_Any",
            _ => v.ToString(),
        },

        ConditionKind v => v switch
        {
            ConditionKind.Extension => "Cond_Type",
            ConditionKind.Name => "Cond_Name",
            ConditionKind.Date => "Cond_Date",
            ConditionKind.Size => "Cond_Size",
            ConditionKind.SourceFolder => "Cond_Source",
            _ => v.ToString(),
        },

        ActionKind v => v switch
        {
            ActionKind.Move => "Act_MoveKind",
            ActionKind.Copy => "Act_CopyKind",
            ActionKind.Rename => "Act_Rename",
            ActionKind.Delete => "Act_DeleteKind",
            ActionKind.Recycle => "Act_RecycleKind",
            ActionKind.RunCommand => "Act_RunKind",
            _ => v.ToString(),
        },

        TemplatePartKind v => v switch
        {
            TemplatePartKind.Literal => "Part_Literal",
            TemplatePartKind.Name => "Part_Name",
            TemplatePartKind.Extension => "Part_Extension",
            TemplatePartKind.Created => "Part_Created",
            TemplatePartKind.Modified => "Part_Modified",
            TemplatePartKind.Today => "Part_Today",
            TemplatePartKind.Counter => "Part_Counter",
            TemplatePartKind.Folder => "Part_Folder",
            TemplatePartKind.FullName => "Part_FullName",
            _ => v.ToString(),
        },

        _ => value.ToString() ?? string.Empty,
    };
}
