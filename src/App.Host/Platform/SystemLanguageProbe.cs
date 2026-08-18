using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Banog.UI.Localization;

namespace Banog.Host.Platform;

/// <summary>
/// La langue d'affichage de Windows, demandée à Windows.
///
/// La solution est compilée en <c>InvariantGlobalization</c> : dans le processus,
/// <c>CurrentUICulture</c> est la culture invariante et ne dit rien de la langue du
/// système. On interroge donc directement l'API, ce qui est de toute façon la source
/// de vérité — c'est la langue dans laquelle Windows lui-même s'affiche.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class SystemLanguageProbe
{
    /// <summary>Identifiant de langue principal du français (LANG_FRENCH).</summary>
    private const int LangFrench = 0x0C;

    [LibraryImport("kernel32.dll")]
    private static partial ushort GetUserDefaultUILanguage();

    public static UiLanguage Current =>
        (GetUserDefaultUILanguage() & 0x3FF) == LangFrench ? UiLanguage.French : UiLanguage.English;
}
