# Stream a joined sequence

`ITextWriter.WriteJoin` accepts an enumerable of strings with a counted or
C-string separator. It enumerates once and writes directly to the borrowed
writer, without building a complete joined string first.

```csharp
using Standard::Collections;
using Standard::Text;

int main()
{
    Vector<String> words = new();
    words.Add(s"one");
    words.Add(s"two");
    String output = new();
    ITextWriter* writer = &output;
    writer.WriteJoin(words.AsEnumerable(), " / ");
    if (!output.Equals("one / two")) return 1;
    return 0;
}
```

Separators go between entries, including empty strings. Counted separators
preserve embedded nulls. C-string separators stop at the first null, and neither
form reads an unused separator for zero or one entry. Empty values and zero-byte
separators cause no writer call. No newline or flush is added.

The helper acquires one iterator and reads each Current once, before writing
that entry's separator. Source and writer errors propagate. A writer may accept
part of a Write before throwing, so failure can leave a partial output prefix.
Iterator and current-value resources are released when the operation exits.
The writer remains borrowed.

Keep separator storage stable and separate from storage modified by the writer.
Source callbacks and writer calls must preserve the remaining input's lifetime
and validity. The helper allocates no join buffer, but source strings returned
by value, iterator acquisition, and the writer itself can allocate.

Selective builds include `Text/enumerable_writer_join.gf` with the usual String
and enumerable dependencies. The free form is
`WriteJoin(writer, values, separator[, separatorCount])`. For an owning result,
see [enumerable string Join](ENUMERABLE_STRING_JOIN.md).
