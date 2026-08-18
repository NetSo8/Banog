using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Banog.Core.Abstractions;
using Banog.Core.Execution;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Banog.UI.ViewModels;

/// <summary>Les morceaux dont un nom (ou un sous-dossier) peut être composé.</summary>
public enum TemplatePartKind
{
    /// <summary>Du texte tapé tel quel : « facture-», « _copie ».</summary>
    Literal = 0,
    Name = 1,
    Extension = 2,
    Created = 3,
    Modified = 4,
    Today = 5,
    Counter = 6,
    Folder = 7,
    FullName = 8,
}

/// <summary>
/// Un format proposé pour une date ou un compteur.
///
/// L'égalité ne porte que sur le motif : le libellé change avec la langue et avec le
/// jour, mais la sélection d'une liste déroulante, elle, doit rester la même.
/// </summary>
public sealed class TemplateFormatOption : IEquatable<TemplateFormatOption>
{
    private readonly string? _descriptionKey;
    private readonly string? _literal;

    internal TemplateFormatOption(string pattern, string? descriptionKey, string? literal = null)
    {
        Pattern = pattern;
        _descriptionKey = descriptionKey;
        _literal = literal;
    }

    public string Pattern { get; }

    /// <summary>« 2026-08-07 (année-mois-jour) ». L'exemple vaut mieux que le motif.</summary>
    public string Label
    {
        get
        {
            if (_literal is not null) return _literal;

            var sample = DateTimeOffset.Now.ToString(Pattern, CultureInfo.InvariantCulture);
            return $"{sample}  ({Localization.Loc.T(_descriptionKey ?? string.Empty)})";
        }
    }

    public bool Equals(TemplateFormatOption? other) => other is not null && other.Pattern == Pattern;

    public override bool Equals(object? obj) => Equals(obj as TemplateFormatOption);

    public override int GetHashCode() => Pattern.GetHashCode(StringComparison.Ordinal);
}

/// <summary>
/// Un morceau du gabarit. Le texte libre s'édite dans la pastille ; un jeton se contente
/// d'un format quand il en accepte un.
/// </summary>
public partial class TemplatePartViewModel : ObservableObject
{
    private readonly bool _rejectDots;
    private bool _cleaning;

    public TemplatePartKind Kind { get; }

    [ObservableProperty] public partial string Text { get; set; } = string.Empty;
    [ObservableProperty] public partial TemplateFormatOption? Format { get; set; }

    public TemplatePartViewModel(
        TemplatePartKind kind, string text = "", TemplateFormatOption? format = null, bool rejectDots = false)
    {
        Kind = kind;
        Text = text;
        Format = format ?? DefaultFormat(kind);
        _rejectDots = rejectDots;
    }

    public bool IsLiteral => Kind == TemplatePartKind.Literal;
    public bool IsToken => Kind != TemplatePartKind.Literal;

    /// <summary>Vrai pour les jetons dont le rendu se règle (dates, compteur).</summary>
    public bool UsesFormat => Kind is TemplatePartKind.Created or TemplatePartKind.Modified
        or TemplatePartKind.Today or TemplatePartKind.Counter;

    public IReadOnlyList<TemplateFormatOption> FormatOptions => Kind == TemplatePartKind.Counter
        ? NameTemplateBuilderViewModel.CounterFormats
        : NameTemplateBuilderViewModel.DateFormats;

    public string Label => Localization.Labels.For(Kind);

    partial void OnTextChanged(string value)
    {
        if (!_rejectDots || !IsLiteral || _cleaning || !value.Contains('.')) return;

        // Le point sépare le nom de l'extension : dans un morceau de nom, il n'a pas sa
        // place. Le retirer aussitôt vaut mieux qu'un avertissement qu'on ne lit pas.
        _cleaning = true;
        try { Text = value.Replace(".", string.Empty); }
        finally { _cleaning = false; }
    }

    /// <summary>Le fragment de gabarit correspondant : c'est ce que le moteur lira.</summary>
    public string ToTemplate() => Kind switch
    {
        TemplatePartKind.Literal => Escape(Text),
        TemplatePartKind.Name => "{nom}",
        TemplatePartKind.FullName => "{fichier}",
        TemplatePartKind.Extension => "{extension}",
        TemplatePartKind.Folder => "{dossier}",
        TemplatePartKind.Created => WithFormat("date"),
        TemplatePartKind.Modified => WithFormat("modification"),
        TemplatePartKind.Today => WithFormat("aujourdhui"),
        TemplatePartKind.Counter => WithFormat("compteur"),
        _ => string.Empty,
    };

    private string WithFormat(string token) => string.IsNullOrEmpty(Format?.Pattern)
        ? $"{{{token}}}"
        : $"{{{token}:{Format.Pattern}}}";

    /// <summary>Une accolade tapée dans du texte libre reste une accolade à l'arrivée.</summary>
    public static string Escape(string text) => text.Replace("{", "{{").Replace("}", "}}");

    private static TemplateFormatOption? DefaultFormat(TemplatePartKind kind) => kind switch
    {
        TemplatePartKind.Created or TemplatePartKind.Modified or TemplatePartKind.Today
            => NameTemplateBuilderViewModel.DateFormats[0],
        TemplatePartKind.Counter => NameTemplateBuilderViewModel.CounterFormats[0],
        _ => null,
    };
}

/// <summary>Ce que le gabarit compose : un nom de fichier, ou un chemin de dossier.</summary>
public enum TemplateTarget
{
    FileName = 0,
    Folder = 1,
}

/// <summary>
/// Composition d'un nom de fichier (ou d'un sous-dossier) morceau par morceau.
///
/// Le moteur lit toujours un gabarit à accolades — « {date}_{nom}.{extension} » — mais
/// personne n'a à l'écrire : on empile des pastilles « Date » et « Nom », on les réordonne,
/// puis l'extension du fichier source est ajoutée automatiquement. Le gabarit reste la
/// source de vérité : il est relu au chargement, et le mode avancé reste disponible pour
/// les formats que les pastilles ne savent pas représenter.
/// </summary>
public partial class NameTemplateBuilderViewModel : ObservableObject
{
    /// <summary>Formats de date proposés, avec le rendu du jour comme libellé.</summary>
    public static TemplateFormatOption[] DateFormats { get; } = BuildDateFormats();

    /// <summary>Les exemples de numérotation se lisent dans toutes les langues.</summary>
    public static TemplateFormatOption[] CounterFormats { get; } =
    [
        new(string.Empty, null, "1, 2, 3…"),
        new("00", null, "01, 02, 03…"),
        new("000", null, "001, 002, 003…"),
    ];

    /// <summary>Morceaux proposés à l'ajout, du plus courant au plus rare. L'extension est
    /// toujours reprise du fichier source et ne constitue donc pas un morceau éditable.</summary>
    public static TemplatePartKind[] PartKinds { get; } =
    [
        TemplatePartKind.Literal,
        TemplatePartKind.Name,
        TemplatePartKind.Created,
        TemplatePartKind.Counter,
        TemplatePartKind.Modified,
        TemplatePartKind.Today,
        TemplatePartKind.Folder,
    ];

    private readonly TemplateTarget _target;
    private bool _composing;
    private bool _parsing;

    public ObservableCollection<TemplatePartViewModel> Parts { get; } = [];

    /// <summary>Le gabarit à accolades. Écrit par les pastilles, relu quand il change de l'extérieur.</summary>
    [ObservableProperty] public partial string Template { get; set; } = string.Empty;

    /// <summary>Édition directe du gabarit. Imposée quand il contient ce que les pastilles ne savent pas rendre.</summary>
    [ObservableProperty] public partial bool IsAdvanced { get; set; }

    /// <summary>Chemin affiché devant l'aperçu (dossier de destination choisi au parcours).</summary>
    [ObservableProperty] public partial string PreviewPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Les morceaux d'un sous-dossier restent repliés tant qu'on n'en demande pas :
    /// la plupart des règles rangent tout dans le même dossier.
    /// </summary>
    [ObservableProperty] public partial bool IsPaletteOpen { get; set; }

    public NameTemplateBuilderViewModel(TemplateTarget target, string template)
    {
        _target = target;
        Template = template;
        Parse(template);

        IsPaletteOpen = target == TemplateTarget.FileName || Parts.Count > 0 || IsAdvanced;

        Parts.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (INotifyPropertyChanged part in e.OldItems) part.PropertyChanged -= OnPartChanged;
            }

            if (e.NewItems is not null)
            {
                foreach (INotifyPropertyChanged part in e.NewItems) part.PropertyChanged += OnPartChanged;
            }

            Compose();
        };

        foreach (INotifyPropertyChanged part in Parts) part.PropertyChanged += OnPartChanged;
    }

    /// <summary>Le composeur ne connaît que deux formes : un nom de fichier, ou un chemin de dossier.</summary>
    public bool IsFileName => _target == TemplateTarget.FileName;

    public bool HasParts => Parts.Count > 0;

    /// <summary>Ce que ce gabarit compose, dit en une ligne au-dessus des pastilles.</summary>
    public string Hint => Localization.Loc.T(
        _target == TemplateTarget.FileName ? "Tpl_Hint_Name" : "Tpl_Hint_Folder");

    public string PreviewLabel => Localization.Loc.T(
        _target == TemplateTarget.FileName ? "Tpl_Preview_Name" : "Tpl_Preview_Folder");

    /// <summary>Libellé du bouton qui déplie les morceaux d'un sous-dossier.</summary>
    public static string OpenPaletteLabel => Localization.Loc.T("Tpl_OpenPalette");

    /// <summary>Le nom (ou le chemin) que produirait le gabarit sur un fichier d'exemple.</summary>
    public string Preview
    {
        get
        {
            var sample = SampleFile();
            var now = DateTimeOffset.Now;

            if (_target == TemplateTarget.FileName)
            {
                return string.IsNullOrEmpty(Template)
                    ? string.Empty
                    : TokenExpander.ExpandFileNameWithOriginalExtension(Template, sample, now, counter: 2);
            }

            var folder = string.IsNullOrEmpty(Template)
                ? string.Empty
                : TokenExpander.Expand(Template, sample, now, 2, TokenScope.Path);

            if (string.IsNullOrWhiteSpace(PreviewPrefix)) return folder;

            return folder.Length == 0
                ? PreviewPrefix
                : System.IO.Path.Join(PreviewPrefix.TrimEnd('\\', '/'), folder);
        }
    }

    /// <summary>
    /// L'aperçu n'apparaît qu'une fois qu'il y a quelque chose à montrer : un dossier de
    /// destination sans sous-dossier se lit déjà dans le champ juste au-dessus.
    /// </summary>
    public bool HasPreview => Preview.Length > 0
        && (_target == TemplateTarget.FileName || HasParts || (IsAdvanced && Template.Length > 0));

    /// <summary>Le fichier d'exemple de l'aperçu, tel qu'on l'annonce à côté.</summary>
    public static string SampleName => Localization.Loc.T("Tpl_SampleName");

    public static string PreviewSample => Localization.Loc.F("Tpl_Preview_Sample", SampleName);

    private void OnPartChanged(object? sender, PropertyChangedEventArgs e) => Compose();

    partial void OnTemplateChanged(string value)
    {
        if (!_composing && !_parsing && _target == TemplateTarget.FileName)
        {
            var normalized = KeepOriginalExtension(value);
            if (!string.Equals(normalized, value, StringComparison.Ordinal))
            {
                _composing = true;
                try { Template = normalized; }
                finally { _composing = false; }
                value = normalized;
            }
        }

        if (!_composing) Parse(value);

        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(HasPreview));
    }

    /// <summary>
    /// Dans le mode avancé, remplace une extension littérale dès qu'elle est complète. Un
    /// utilisateur peut encore écrire le nom librement, mais « .txt » devient toujours
    /// « .{extension} » avant d'être enregistré.
    /// </summary>
    private static string KeepOriginalExtension(string template)
    {
        if (template.EndsWith(".{extension}", StringComparison.OrdinalIgnoreCase)
            || template.EndsWith(".{ext}", StringComparison.OrdinalIgnoreCase)
            || template.EndsWith("{filename}", StringComparison.OrdinalIgnoreCase)
            || template.EndsWith("{fichier}", StringComparison.OrdinalIgnoreCase)) return template;

        var depth = 0;
        var dot = -1;

        for (var i = 0; i < template.Length; i++)
        {
            switch (template[i])
            {
                case '{' when i + 1 < template.Length && template[i + 1] == '{':
                    i++;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth = Math.Max(0, depth - 1);
                    break;
                case '.' when depth == 0:
                    dot = i;
                    break;
            }
        }

        if (dot < 0 || dot == template.Length - 1) return template;
        return string.Concat(template[..dot], ".{extension}");
    }

    partial void OnPreviewPrefixChanged(string value)
    {
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(HasPreview));
    }

    // ---- Édition ---------------------------------------------------------------------

    [RelayCommand]
    private void OpenPalette() => IsPaletteOpen = true;

    partial void OnIsPaletteOpenChanged(bool value) => OnPropertyChanged(nameof(HasPreview));

    partial void OnIsAdvancedChanged(bool value)
    {
        OnPropertyChanged(nameof(HasPreview));
    }

    [RelayCommand]
    private void AddPart(TemplatePartKind kind)
    {
        // Deux textes libres côte à côte sont indiscernables à l'écran : on prolonge le
        // dernier plutôt que d'en empiler un second vide.
        if (kind == TemplatePartKind.Literal && Parts.LastOrDefault() is { IsLiteral: true }) return;

        Parts.Add(new TemplatePartViewModel(kind, rejectDots: _target == TemplateTarget.FileName));
    }

    [RelayCommand]
    private void RemovePart(TemplatePartViewModel? part)
    {
        if (part is not null) Parts.Remove(part);
    }

    /// <summary>
    /// Réordonne par glisser-déposer. L'index est la position finale du morceau ; rien ne
    /// peut dépasser le dernier, et l'extension n'est pas dans la liste.
    /// </summary>
    public void MovePartTo(TemplatePartViewModel part, int insertionIndex)
    {
        var from = Parts.IndexOf(part);
        if (from < 0) return;

        insertionIndex = Math.Clamp(insertionIndex, 0, Parts.Count - 1);
        if (from == insertionIndex) return;

        Parts.Move(from, insertionIndex);
    }

    /// <summary>Retire les morceaux un à un : un vidage en bloc laisserait des abonnements derrière lui.</summary>
    [RelayCommand]
    private void Clear()
    {
        while (Parts.Count > 0) Parts.RemoveAt(Parts.Count - 1);
    }

    [RelayCommand]
    private void ToggleAdvanced()
    {
        // Revenir au mode pastilles relit le gabarit : ce qui a été tapé à la main est
        // conservé s'il est représentable, et le mode reste avancé sinon.
        if (IsAdvanced) Parse(Template);
        else IsAdvanced = true;
    }

    // ---- Gabarit <-> pastilles ---------------------------------------------------------

    private void Compose()
    {
        // Pendant une relecture, les pastilles suivent le gabarit et non l'inverse.
        if (_parsing) return;

        var builder = new StringBuilder();
        foreach (var part in Parts) builder.Append(part.ToTemplate());

        // Le nom de fichier finit toujours par l'extension du fichier source : rien ne
        // permet de la retirer ou de la remplacer dans le composeur.
        if (_target == TemplateTarget.FileName)
        {
            builder.Append('.');
            builder.Append("{extension}");
        }

        _composing = true;
        try
        {
            Template = builder.ToString();
        }
        finally
        {
            _composing = false;
        }

        OnPropertyChanged(nameof(HasParts));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(HasPreview));
    }

    /// <summary>
    /// Relit un gabarit en pastilles. Un gabarit écrit par une autre version — ou à la
    /// main — peut contenir un jeton inconnu : il bascule alors en mode avancé plutôt
    /// que d'être réécrit de travers.
    /// </summary>
    private void Parse(string template)
    {
        var parsed = TryParse(template, out var parts);

        _parsing = true;
        try
        {
            while (Parts.Count > 0) Parts.RemoveAt(Parts.Count - 1);

            if (parsed)
            {
                foreach (var part in parts) Parts.Add(part);

            }
        }
        finally
        {
            _parsing = false;
        }

        IsAdvanced = !parsed;

        OnPropertyChanged(nameof(HasParts));
    }

    private bool TryParse(string template, out List<TemplatePartViewModel> parts)
    {
        var result = new List<TemplatePartViewModel>();
        parts = result;

        if (string.IsNullOrEmpty(template)) return true;

        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length == 0) return;

            result.Add(new TemplatePartViewModel(TemplatePartKind.Literal, literal.ToString()));
            literal.Clear();
        }

        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];

            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                literal.Append('{');
                i += 2;
                continue;
            }

            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                literal.Append('}');
                i += 2;
                continue;
            }

            if (c != '{')
            {
                literal.Append(c);
                i++;
                continue;
            }

            var close = template.IndexOf('}', i + 1);
            if (close < 0) return false;

            var token = template[(i + 1)..close];
            var colon = token.IndexOf(':');
            var name = colon >= 0 ? token[..colon] : token;
            var format = colon >= 0 ? token[(colon + 1)..] : string.Empty;

            if (!TryResolve(name, format, out var part)) return false;

            FlushLiteral();
            result.Add(part);
            i = close + 1;
        }

        FlushLiteral();

        return _target != TemplateTarget.FileName || NormalizeFileName(result);
    }

    /// <summary>
    /// Un nom de fichier est « des morceaux, puis le point, puis l'extension ». Tout
    /// point dans les morceaux eux-mêmes, ou toute extension pas en dernière position,
    /// n'est pas représentable : le mode avancé prend le relais sans rien réécrire.
    /// </summary>
    private static bool NormalizeFileName(List<TemplatePartViewModel> parts)
    {
        var last = parts.Count - 1;
        if (last < 0) return true;

        if (parts[last].Kind == TemplatePartKind.Extension)
        {
            // « {extension} » doit suivre le point, rien d'autre : « {nom}.{extension} »
            // est la forme canonique. Seul, ou sans point, le gabarit passe en mode avancé.
            if (last == 0 || parts[last - 1] is not { IsLiteral: true, Text: "." }) return false;

            parts.RemoveAt(last);
            parts.RemoveAt(last - 1);
            last = parts.Count - 1;
        }
        else if (parts[last] is { IsLiteral: true, Text: { } text })
        {
            var dot = text.LastIndexOf('.');

            if (dot >= 0)
            {
                // La fin après le dernier point est l'extension écrite en toutes lettres :
                // « {nom}.pdf » se lit « nom » + extension « pdf ». Un point avant
                // (« .old.txt ») ne se représente pas.
                var prefix = text[..dot];
                if (prefix.Contains('.') || dot >= text.Length - 1) return false;

                if (prefix.Length > 0)
                {
                    parts[last] = new TemplatePartViewModel(TemplatePartKind.Literal, prefix);
                }
                else
                {
                    if (last == 0) return false;
                    parts.RemoveAt(last);
                }

                last--;
            }
        }

        for (var i = 0; i <= last; i++)
        {
            if (parts[i].Kind == TemplatePartKind.Extension) return false;
            if (parts[i] is { IsLiteral: true, Text: { } t } && t.Contains('.')) return false;

            // {filename} contenait déjà l'extension ; le remplace par {name} puisque le
            // composeur ajoute maintenant l'extension source, une seule fois, à la fin.
            if (parts[i].Kind == TemplatePartKind.FullName)
            {
                parts[i] = new TemplatePartViewModel(TemplatePartKind.Name);
            }
        }

        return true;
    }

    private static bool TryResolve(string name, string format, out TemplatePartViewModel part)
    {
        part = null!;

        var kind = name.ToLowerInvariant() switch
        {
            "nom" or "name" => TemplatePartKind.Name,
            "fichier" or "filename" => TemplatePartKind.FullName,
            "extension" or "ext" => TemplatePartKind.Extension,
            "dossier" or "folder" => TemplatePartKind.Folder,
            "date" or "created" or "creation" or "création" => TemplatePartKind.Created,
            "modification" or "modified" => TemplatePartKind.Modified,
            "aujourdhui" or "now" => TemplatePartKind.Today,
            "compteur" or "counter" => TemplatePartKind.Counter,
            _ => (TemplatePartKind)(-1),
        };

        if ((int)kind < 0) return false;

        TemplateFormatOption? option = null;

        if (kind is TemplatePartKind.Created or TemplatePartKind.Modified or TemplatePartKind.Today)
        {
            option = DateFormats.FirstOrDefault(f => f.Pattern == format)
                ?? (format.Length == 0 ? DateFormats[0] : null);

            // Un format de date personnalisé se garde tel quel, mais il ne se règle pas
            // dans une liste : le mode avancé reste le bon endroit pour l'écrire.
            if (option is null) return false;
        }
        else if (kind == TemplatePartKind.Counter)
        {
            option = CounterFormats.FirstOrDefault(f => f.Pattern == format);
            if (option is null) return false;
        }
        else if (format.Length > 0)
        {
            return false;
        }

        part = new TemplatePartViewModel(kind, string.Empty, option);
        return true;
    }

    // ---- Aperçu ------------------------------------------------------------------------

    /// <summary>
    /// Fichier d'exemple de l'aperçu : un nom reconnaissable, dans un dossier plausible,
    /// daté d'aujourd'hui pour que la date affichée soit celle qu'on attend.
    /// </summary>
    private static FileContext SampleFile()
    {
        // Créé et modifié aujourd'hui : les trois dates affichées coïncident avec les
        // exemples de la liste des formats, sinon l'aperçu semble se contredire.
        var now = DateTimeOffset.Now;
        var directory = Localization.Loc.T("Tpl_SampleFolder");

        return new FileContext(
            System.IO.Path.Join(directory, SampleName), directory, 2_400_000, now, now);
    }

    private static TemplateFormatOption[] BuildDateFormats() =>
    [
        new("yyyy-MM-dd", "Fmt_Ymd"),
        new("yyyy-MM", "Fmt_Ym"),
        new("yyyy", "Fmt_Y"),
        new("MM", "Fmt_M"),
        new("dd", "Fmt_D"),
        new("yyyyMMdd", "Fmt_Compact"),
        new("yyyy-MM-dd_HH-mm", "Fmt_WithTime"),
    ];
}
