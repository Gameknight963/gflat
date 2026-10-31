# Splitting on a byte set

`text.SplitAny(separators, removeEmpty = false)` splits a String or character span
at any byte in the separator set.

```csharp
using Standard::Text;
using Standard;
using Standard::Memory;

int main()
{
    String text = s"one,two;three";
    nuint count = 0;
    foreach (Span<readonly(char)> part in text.SplitAny(",;"))
    {
        if (part.Length != 3 && part.Length != 5) return 1;
        count++;
    }
    return (int)count - 3;
}
```

Separators can be a C string or `Span<readonly(char)>`. The span form can include
NUL. Set order and duplicate bytes have no effect. Construction copies the set,
so separator storage can be changed or released afterward.

Leading, repeated, and trailing delimiters preserve empty parts by default.
Empty input yields one empty part. `removeEmpty` skips them all. An empty
separator set leaves the input as one part.

The result is `TextPartsAny`. Each enumerator starts at the beginning and returns
borrowed readonly spans through `Current`. `AsEnumerable()` enables queries.

Retain the source owner while using parts or iterators. Mutating source bytes can
change later delimiter matches. Views from temporary Strings last only through
that expression.

Sources in `core/Text`: `TextPartsAny.gf`, `text_parts_any_span.gf`, and
`string_split_any.gf`.
