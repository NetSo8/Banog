using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Banog.UI.Services;

/// <summary>Sélecteur de dossier adossé au StorageProvider Avalonia de la fenêtre hôte.</summary>
public sealed class StorageProviderFolderPicker(TopLevel topLevel) : IFolderPicker
{
    public async Task<string?> PickFolderAsync(string title)
    {
        var results = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        var folder = results.Count > 0 ? results[0] : null;
        return folder?.TryGetLocalPath();
    }

    public async Task<string?> PickFileAsync(string title, string? filter = null, string? extension = null)
    {
        var options = new FilePickerOpenOptions { Title = title, AllowMultiple = false };

        if (filter is not null && extension is not null)
        {
            options.FileTypeFilter = [new FilePickerFileType(filter) { Patterns = [extension] }];
        }

        var results = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        var file = results.Count > 0 ? results[0] : null;
        return file?.TryGetLocalPath();
    }
}
