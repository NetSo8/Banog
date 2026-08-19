using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Banog.Host.Platform;

/// <summary>
/// Démarrage avec Windows, par la clé Run de l'utilisateur courant. La valeur pointe vers
/// l'exécutable en train de tourner, avec <c>--background</c> : à l'ouverture de session,
/// Banog se lance sans fenêtre, réduit à la zone de notification.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Banog";

    public void EnsureRegistered()
    {
        // CreateSubKey crée la branche Run si elle n'existe pas encore (profil neuf) :
        // la clé doit être écrite quoi qu'il arrive, pas seulement quand elle préexiste.
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

        // Le chemin de l'exécutable courant, jamais celui d'une copie déplacée :
        // réappliquer au démarrage réaligne la clé sur l'installation réelle.
        if (Environment.ProcessPath is { } executable)
        {
            key.SetValue(ValueName, $"\"{executable}\" --background", RegistryValueKind.String);
        }
    }
}
