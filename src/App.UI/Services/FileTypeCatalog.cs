using System.Globalization;
using System.Text;
using Banog.UI.Localization;

namespace Banog.UI.Services;

/// <summary>
/// Un type de fichier proposé dans le sélecteur : l'extension telle qu'elle sera écrite
/// dans la règle, un libellé lisible, et une catégorie.
///
/// Le libellé et la catégorie sont lus à l'affichage, pas figés à la construction : le
/// catalogue est statique et survit à un changement de langue.
/// </summary>
public sealed class FileTypeOption
{
    private readonly string _labelFr;
    private readonly string _labelEn;

    /// <summary>Vrai pour une extension absente du catalogue : son libellé se traduit à l'affichage.</summary>
    private readonly bool _unknown;

    internal FileTypeOption(
        string extension,
        string labelFr,
        string labelEn,
        string categoryKey,
        string keywords = "",
        bool unknown = false)
    {
        _unknown = unknown;
        Extension = extension;
        CategoryKey = categoryKey;
        _labelFr = labelFr;
        _labelEn = labelEn;
        Badge = extension.ToUpperInvariant();

        // L'index contient les deux langues : « photo » et « picture » doivent trouver la
        // même chose, quelle que soit la langue affichée au moment de la recherche.
        SearchIndex = FileTypeCatalog.Normalize(
            $"{extension} {labelFr} {labelEn} {FileTypeCatalog.CategoryNames(categoryKey)} {keywords}");
    }

    /// <summary>Extension sans le point, en minuscules. C'est la valeur stockée dans la règle.</summary>
    public string Extension { get; }

    /// <summary>L'extension telle qu'on l'affiche dans une pastille : « PDF », « JPG ».</summary>
    public string Badge { get; }

    /// <summary>Clé de la catégorie d'affichage.</summary>
    public string CategoryKey { get; }

    /// <summary>Ce sur quoi la recherche compare, normalisé une seule fois.</summary>
    public string SearchIndex { get; }

    /// <summary>Ce que la personne reconnaît : « Photo JPEG », « Document Word ».</summary>
    public string Label => _unknown
        ? Loc.F("Type_Unknown", Extension)
        : Loc.Current == UiLanguage.French ? _labelFr : _labelEn;
}

/// <summary>
/// Liste des types de fichiers courants, groupés par usage.
///
/// Écrire « pdf, png, jpg » dans un champ libre suppose qu'on connaisse déjà les
/// extensions ; la plupart des gens connaissent « photo » ou « vidéo ». Le catalogue
/// permet de chercher par l'un ou par l'autre, et une extension absente d'ici reste
/// toujours ajoutable à la main.
/// </summary>
public static class FileTypeCatalog
{
    public const string Images = "Cat_Images";
    public const string Documents = "Cat_Documents";
    public const string Spreadsheets = "Cat_Spreadsheets";
    public const string Presentations = "Cat_Presentations";
    public const string Videos = "Cat_Videos";
    public const string Audio = "Cat_Audio";
    public const string Archives = "Cat_Archives";
    public const string Code = "Cat_Code";
    public const string System = "Cat_System";

    /// <summary>Ordre d'affichage des catégories : du plus courant au plus technique.</summary>
    public static string[] Categories { get; } =
    [
        Images, Documents, Spreadsheets, Presentations, Videos, Audio, Archives, Code, System,
    ];

    /// <summary>Les deux noms d'une catégorie, pour l'index de recherche.</summary>
    internal static string CategoryNames(string key) => key switch
    {
        Images => "images",
        Documents => "documents",
        Spreadsheets => "tableurs spreadsheets",
        Presentations => "présentations presentations",
        Videos => "vidéos videos",
        Audio => "audio",
        Archives => "archives",
        Code => "code web",
        System => "programmes système programs system",
        _ => string.Empty,
    };

    public static FileTypeOption[] All { get; } =
    [
        new("jpg", "Photo JPEG", "JPEG photo", Images, "photo image appareil picture camera"),
        new("jpeg", "Photo JPEG", "JPEG photo", Images, "photo image picture"),
        new("png", "Image PNG", "PNG image", Images, "image capture écran transparence screenshot"),
        new("gif", "Image animée GIF", "Animated GIF", Images, "image animation"),
        new("webp", "Image WebP", "WebP image", Images, "image web"),
        new("bmp", "Image bitmap", "Bitmap image", Images, "image"),
        new("tif", "Image TIFF", "TIFF image", Images, "image scan numérisation"),
        new("tiff", "Image TIFF", "TIFF image", Images, "image scan numérisation"),
        new("heic", "Photo iPhone", "iPhone photo", Images, "photo apple image picture"),
        new("svg", "Image vectorielle SVG", "SVG vector image", Images, "vectoriel dessin logo drawing"),
        new("ico", "Icône", "Icon", Images, "image"),
        new("raw", "Photo brute RAW", "RAW photo", Images, "photo appareil camera"),
        new("cr2", "Photo brute Canon", "Canon RAW photo", Images, "photo appareil raw camera"),
        new("nef", "Photo brute Nikon", "Nikon RAW photo", Images, "photo appareil raw camera"),
        new("psd", "Document Photoshop", "Photoshop document", Images, "image montage adobe"),
        new("ai", "Document Illustrator", "Illustrator document", Images, "vectoriel dessin adobe"),

        new("pdf", "Document PDF", "PDF document", Documents, "facture papier scan contrat invoice"),
        new("doc", "Document Word", "Word document", Documents, "texte traitement de texte"),
        new("docx", "Document Word", "Word document", Documents, "texte traitement de texte"),
        new("odt", "Document LibreOffice", "LibreOffice document", Documents, "texte open document"),
        new("rtf", "Texte enrichi", "Rich text", Documents, "texte"),
        new("txt", "Texte brut", "Plain text", Documents, "note texte"),
        new("md", "Texte Markdown", "Markdown text", Documents, "note texte"),
        new("epub", "Livre numérique", "E-book", Documents, "ebook lecture liseuse reader"),
        new("mobi", "Livre Kindle", "Kindle book", Documents, "ebook lecture liseuse reader"),

        new("xls", "Classeur Excel", "Excel workbook", Spreadsheets, "tableau calcul sheet"),
        new("xlsx", "Classeur Excel", "Excel workbook", Spreadsheets, "tableau calcul sheet"),
        new("ods", "Classeur LibreOffice", "LibreOffice workbook", Spreadsheets, "tableau calcul sheet"),
        new("csv", "Tableau CSV", "CSV table", Spreadsheets, "tableau données export data"),
        new("tsv", "Tableau TSV", "TSV table", Spreadsheets, "tableau données export data"),

        new("ppt", "Présentation PowerPoint", "PowerPoint presentation", Presentations, "diaporama slides"),
        new("pptx", "Présentation PowerPoint", "PowerPoint presentation", Presentations, "diaporama slides"),
        new("odp", "Présentation LibreOffice", "LibreOffice presentation", Presentations, "diaporama slides"),
        new("key", "Présentation Keynote", "Keynote presentation", Presentations, "diaporama slides apple"),

        new("mp4", "Vidéo MP4", "MP4 video", Videos, "film video movie"),
        new("mkv", "Vidéo MKV", "MKV video", Videos, "film video movie"),
        new("avi", "Vidéo AVI", "AVI video", Videos, "film video movie"),
        new("mov", "Vidéo QuickTime", "QuickTime video", Videos, "film video apple movie"),
        new("wmv", "Vidéo Windows Media", "Windows Media video", Videos, "film video movie"),
        new("webm", "Vidéo WebM", "WebM video", Videos, "film video web"),
        new("flv", "Vidéo Flash", "Flash video", Videos, "film video movie"),
        new("m4v", "Vidéo M4V", "M4V video", Videos, "film video movie"),
        new("mpg", "Vidéo MPEG", "MPEG video", Videos, "film video movie"),

        new("mp3", "Musique MP3", "MP3 music", Audio, "musique son audio sound"),
        new("wav", "Son WAV", "WAV sound", Audio, "musique son audio sound"),
        new("flac", "Musique FLAC", "FLAC music", Audio, "musique son audio lossless"),
        new("aac", "Musique AAC", "AAC music", Audio, "musique son audio sound"),
        new("m4a", "Musique M4A", "M4A music", Audio, "musique son audio apple"),
        new("ogg", "Musique OGG", "OGG music", Audio, "musique son audio sound"),
        new("wma", "Musique WMA", "WMA music", Audio, "musique son audio sound"),
        new("aiff", "Son AIFF", "AIFF sound", Audio, "musique son audio sound"),
        new("mid", "Fichier MIDI", "MIDI file", Audio, "musique son music"),

        new("zip", "Archive ZIP", "ZIP archive", Archives, "compressé dossier compressed"),
        new("rar", "Archive RAR", "RAR archive", Archives, "compressé compressed"),
        new("7z", "Archive 7-Zip", "7-Zip archive", Archives, "compressé compressed"),
        new("tar", "Archive TAR", "TAR archive", Archives, "compressé compressed"),
        new("gz", "Archive GZIP", "GZIP archive", Archives, "compressé compressed"),
        new("bz2", "Archive BZIP2", "BZIP2 archive", Archives, "compressé compressed"),
        new("xz", "Archive XZ", "XZ archive", Archives, "compressé compressed"),
        new("iso", "Image disque ISO", "ISO disc image", Archives, "disque cd dvd disc"),

        new("html", "Page web", "Web page", Code, "web site"),
        new("css", "Feuille de style", "Style sheet", Code, "web site"),
        new("js", "Script JavaScript", "JavaScript script", Code, "web programme program"),
        new("ts", "Script TypeScript", "TypeScript script", Code, "web programme program"),
        new("json", "Données JSON", "JSON data", Code, "données configuration data"),
        new("xml", "Données XML", "XML data", Code, "données configuration data"),
        new("yaml", "Données YAML", "YAML data", Code, "données configuration data"),
        new("yml", "Données YAML", "YAML data", Code, "données configuration data"),
        new("sql", "Script SQL", "SQL script", Code, "base de données database"),
        new("py", "Script Python", "Python script", Code, "programme program"),
        new("cs", "Source C#", "C# source", Code, "programme program"),
        new("java", "Source Java", "Java source", Code, "programme program"),
        new("c", "Source C", "C source", Code, "programme program"),
        new("cpp", "Source C++", "C++ source", Code, "programme program"),
        new("h", "En-tête C", "C header", Code, "programme program"),
        new("sh", "Script shell", "Shell script", Code, "programme program"),
        new("ps1", "Script PowerShell", "PowerShell script", Code, "programme program"),

        new("exe", "Programme Windows", "Windows program", System, "installateur application installer"),
        new("msi", "Installateur Windows", "Windows installer", System, "installateur application"),
        new("apk", "Application Android", "Android app", System, "installateur application installer"),
        new("dmg", "Image disque macOS", "macOS disc image", System, "installateur application apple"),
        new("bat", "Fichier de commandes", "Batch file", System, "script"),
        new("cmd", "Fichier de commandes", "Batch file", System, "script"),
        new("dll", "Bibliothèque Windows", "Windows library", System, "système system"),
        new("lnk", "Raccourci", "Shortcut", System, "système system"),
        new("log", "Journal", "Log", System, "trace texte"),
        new("ini", "Réglages INI", "INI settings", System, "configuration"),
        new("cfg", "Réglages", "Settings", System, "configuration"),
        new("tmp", "Fichier temporaire", "Temporary file", System, "temporaire temp"),
        new("torrent", "Fichier torrent", "Torrent file", System, "téléchargement download"),
    ];

    /// <summary>Le catalogue indexé par extension, pour retrouver le libellé d'une pastille.</summary>
    private static readonly Dictionary<string, FileTypeOption> ByExtension =
        All.GroupBy(o => o.Extension, StringComparer.OrdinalIgnoreCase)
           .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public static FileTypeOption? Find(string extension)
        => ByExtension.TryGetValue(extension, out var option) ? option : null;

    /// <summary>
    /// L'entrée de catalogue d'une extension. Une extension inconnue reste utilisable :
    /// elle reçoit une entrée synthétique plutôt qu'un refus.
    /// </summary>
    public static FileTypeOption Resolve(string extension)
    {
        if (Find(extension) is { } known) return known;

        return new FileTypeOption(extension, string.Empty, string.Empty, "Cat_Other", unknown: true);
    }

    /// <summary>
    /// Nettoie ce que quelqu'un tape : « .PDF », « *.pdf » et « pdf » désignent la même chose.
    /// </summary>
    public static string NormalizeExtension(string raw)
        => raw.Trim().TrimStart('*', '.').Trim().ToLowerInvariant();

    /// <summary>
    /// Comparaison sans accents ni casse : « video » doit trouver « Vidéos », qu'on ait
    /// pensé à l'accent ou non.
    /// </summary>
    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
