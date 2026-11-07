# Using owning strings as search arguments

`String` accepts `immutable(String)*` arguments for `IndexOf`, `LastIndexOf`,
`Contains`, `StartsWith`, and `EndsWith`. `Replace` accepts borrowed old and
replacement strings. These overloads belong to `String.gf`.

```csharp
using Standard::Text;

int main()
{
    String text = s"one::two";
    String separator = s"::";
    if (text.IndexOf(&separator) != 3 || !text.Contains(&separator)) return 1;
    String replacement = s"/";
    text.Replace(&separator, &replacement);
    if (!text.Equals("one/two")) return 2;
    return 0;
}
```

Arguments are borrowed and include every counted byte, including embedded
zeros. Matching compares exact bytes without Unicode normalization or case
folding. Search overloads allocate no storage. An absent index is
`String::NotFound`; `IndexOf` also accepts a starting byte index and throws
when it exceeds `Length`.

Empty patterns follow the existing counted-buffer operations: containment
and prefix/suffix checks succeed, `IndexOf` returns its start, and `LastIndexOf`
returns `Length`. Replacing an empty old pattern throws. Replacement mutates
the receiver using the existing staged replacement operation. Either argument
may refer to the receiver itself; allocation failure leaves it unchanged.
The borrowed arguments are not copied into separate owning strings.
