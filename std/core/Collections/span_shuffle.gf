namespace std::Collections;

// Fisher-Yates shuffle. Borrows the elements and advances the caller's generator.
// Relocation supports owning elements without invoking copy constructors.
public void Shuffle<T>(Span<T> values, ref PseudoRandom random)
{
    nuint remaining = values.Length;
    if (remaining < 2) return;
    T* data = values.Data!;
    while (remaining > 1)
    {
        nuint selected = (nuint)random.NextULong((ulong)remaining);
        remaining--;
        if (selected == remaining) continue;
        T temporary = move *(data + remaining);
        move *(data + selected) at data + remaining;
        move temporary at data + selected;
    }
}
