using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Banog.UI.Services;

/// <summary>
/// Icône de la zone de notification, dessinée au démarrage plutôt qu'embarquée en
/// ressource : un carré bleu arrondi portant un dossier blanc. Rien à binariser, rien à
/// embarquer — et le pixel étant écrit à la main, l'icône est la même à toutes les
/// échelles que Windows impose.
/// </summary>
public static class TrayIconImage
{
    private const int Size = 32;
    private const double Radius = 7;

    // Bleu accent du thème sombre : l'icône vit dans la barre des tâches, pas dans l'app.
    private const byte AccentBlue = 0xFF;
    private const byte AccentGreen = 0xC2;
    private const byte AccentRed = 0x4C;

    public static WindowIcon Create() => new(Draw());

    private static WriteableBitmap Draw()
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(Size, Size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

        using (var buffer = bitmap.Lock())
        {
            var pixels = new byte[buffer.RowBytes * Size];

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    if (InFolder(x, y)) Set(pixels, x, y, buffer.RowBytes, 0xFF, 0xFF, 0xFF, 0xFF);
                    else if (InRoundedSquare(x, y)) Set(pixels, x, y, buffer.RowBytes, AccentBlue, AccentGreen, AccentRed, 0xFF);
                }
            }

            Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }

        return bitmap;
    }

    /// <summary>Fond : carré aux coins arrondis, plein cadre.</summary>
    private static bool InRoundedSquare(int x, int y)
    {
        // Le centre du coin le plus proche, puis la distance à ce centre.
        var cx = x < Radius ? Radius : x > Size - 1 - Radius ? Size - 1 - Radius : x;
        var cy = y < Radius ? Radius : y > Size - 1 - Radius ? Size - 1 - Radius : y;

        var dx = x - cx;
        var dy = y - cy;

        return (dx * dx) + (dy * dy) <= Radius * Radius;
    }

    /// <summary>Dossier blanc : une languette posée sur un corps.</summary>
    private static bool InFolder(int x, int y) =>
        (x >= 7 && x <= 17 && y >= 9 && y <= 13) || (x >= 7 && x <= 25 && y >= 13 && y <= 24);

    private static void Set(byte[] pixels, int x, int y, int rowBytes, byte b, byte g, byte r, byte a)
    {
        var index = (y * rowBytes) + (x * 4);

        pixels[index] = b;
        pixels[index + 1] = g;
        pixels[index + 2] = r;
        pixels[index + 3] = a;
    }
}
