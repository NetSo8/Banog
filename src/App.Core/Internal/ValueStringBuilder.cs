using System.Buffers;
using System.Runtime.CompilerServices;

namespace Banog.Core.Internal;

/// <summary>
/// Tampon de composition de chaînes vivant sur la pile.
///
/// Un <see cref="System.Text.StringBuilder"/> alloue au minimum l'objet plus son tableau de
/// caractères à chaque appel. Ici l'appelant fournit un <c>stackalloc</c> : le cas courant
/// (un nom de fichier, un chemin) n'alloue rien du tout. Le débordement bascule sur
/// <see cref="ArrayPool{T}"/>, donc toujours pas d'allocation LOH ni de pression GC.
///
/// C'est un <c>ref struct</c> : il ne peut ni s'échapper vers le tas, ni traverser un
/// <c>await</c>. <see cref="Dispose"/> doit être appelé (via <c>using</c>) pour rendre le
/// tableau au pool.
/// </summary>
internal ref struct ValueStringBuilder
{
    private char[]? _rented;
    private Span<char> _buffer;
    private int _length;

    public ValueStringBuilder(Span<char> initialBuffer)
    {
        _rented = null;
        _buffer = initialBuffer;
        _length = 0;
    }

    public readonly int Length => _length;

    public void Append(char value)
    {
        if (_length >= _buffer.Length) Grow(1);
        _buffer[_length++] = value;
    }

    public void Append(scoped ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return;
        if (_length + value.Length > _buffer.Length) Grow(value.Length);

        value.CopyTo(_buffer[_length..]);
        _length += value.Length;
    }

    /// <summary>Réserve <paramref name="count"/> caractères et rend la tranche à remplir.</summary>
    public Span<char> AppendSpan(int count)
    {
        if (_length + count > _buffer.Length) Grow(count);

        var slice = _buffer.Slice(_length, count);
        _length += count;
        return slice;
    }

    /// <summary>Annule les <paramref name="count"/> derniers caractères écrits.</summary>
    public void Rewind(int count) => _length -= count;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int additional)
    {
        var capacity = Math.Max(_buffer.Length * 2, _length + additional);
        var next = ArrayPool<char>.Shared.Rent(capacity);

        _buffer[.._length].CopyTo(next);

        var previous = _rented;
        _rented = next;
        _buffer = next;

        if (previous is not null) ArrayPool<char>.Shared.Return(previous);
    }

    public readonly ReadOnlySpan<char> AsSpan() => _buffer[.._length];

    public override readonly string ToString() => new(_buffer[.._length]);

    public void Dispose()
    {
        var rented = _rented;
        this = default;
        if (rented is not null) ArrayPool<char>.Shared.Return(rented);
    }
}
