# Join three or four path components

`Paths.Join` and `Paths.Combine` accept three or four C-string components or
three or four `Span<readonly(char)>` components, followed by PathStyle.
They return an owning String and reserve its final storage once.

```csharp
using Standard::IO;
using Standard::Text;

int main()
{
    String joined = Paths::Join("assets", "images", "icons", "app.png", PathStyle::Unix);
    if (!joined.Equals("assets/images/icons/app.png")) return 1;
    String combined = Paths::Combine("ignored", "/root", "file.txt", PathStyle::Unix);
    if (!combined.Equals("/root/file.txt")) return 2;
    return 0;
}
```

The results follow repeated applications of the existing two-component
operations. Empty components are skipped. Join retains later rooted components
as text and collapses one separator at a boundary where both components have
one. Combine starts at the last rooted component, including a Windows
drive-relative component such as `C:folder`.

The explicit Unix or Windows style applies on every host. Dot segments and
interior repeated separators are not normalized. Counted spans preserve
embedded zero bytes. C-string inputs stop at their first zero.

All-empty input returns an empty String without allocation. A nonempty result
uses one storage allocation. Allocation failure leaves inputs and a previous
assignment destination unchanged. Input storage must stay alive and stable
through the call. Length overflow throws before reserving output storage.

Selective builds use the normal `IO/Paths.gf`, `IO/PathStyle.gf`, String, and
Span dependencies. String span views additionally use `Text/string_span.gf`.
