# Lazy distinct values

`Distinct()` yields the first occurrence of each value in encounter order:

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;
using Standard::Collections;

int main()
{
    int[6] data = [3, 1, 3, 2, 1, 2];
    Span<int> values = new(data, 6);
    Vector<int> unique = values.AsEnumerable().Distinct().ToVector();
    if (unique.Count != 3 || unique[0] != 3 || unique[1] != 1 || unique[2] != 2)
        return 1;
    return 0;
}
```

Values must implement `IHashable` and `IEquatable<T>` with stable behavior.
Equal values need equal hashes. Each iterator owns a hash set that grows with
the number and size of distinct values.

## Evaluation

Each move reads candidates until it finds an unseen value. `Current` then
reads that source position again. A preceding selector can therefore run
during the move and on every `Current` read. Use a selector whose value stays
consistent at each position.

`Count()` and `Any()` still inspect candidate values to establish distinctness.
After a failed move, discard the iterator. Its source may already have advanced
and its set may contain earlier keys.

## Unique file lines

```csharp
using Standard::Text;
using Standard;
using Standard::IO;
using Standard::Queries;

int main()
{
    FileStream input = File::OpenRead("input.txt");
    FileStream output = File::OpenWrite("unique.txt");
    FileStream* source = &input;
    foreach (String line in source.Lines().AsEnumerable().Distinct())
    {
        output.Write(line.Data, line.Length);
        output.WriteLine();
    }
    input.Close();
    output.Close();
    return 0;
}
```

The line reader reuses its current String. Distinct stores independent copies
of seen lines. Memory grows with the unique line contents. LF and CRLF input
endings are removed, and each output line receives one LF. Bare CR and embedded
NUL remain part of a line.

Each enumeration gets a new seen-key set. The source determines where it
starts: a span restarts, while a file-line view continues at the file's current
position. Keep borrowed source storage alive. See [query ownership](QUERIES.md#ownership).

Sources: `Queries/enumerable_distinct.gf`, `Queries/DistinctSource.gf`, and
[HashSet](HASH_COLLECTIONS.md) with its dependencies.
