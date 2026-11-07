# Combining path components

`Paths::Combine(left, right, style)` returns an owning string. A rooted right
component replaces the left component. Otherwise, the components are joined with
a separator when needed.

```csharp
using Standard::IO;
using Standard::Text;

int main()
{
    String relative = Paths::Combine("assets", "icons/logo.png", PathStyle::Unix);
    if (!relative.Equals("assets/icons/logo.png")) return 1;
    String absolute = Paths::Combine("assets", "/shared/logo.png", PathStyle::Unix);
    if (!absolute.Equals("/shared/logo.png")) return 2;
    String drive = Paths::Combine("C:\\work", "D:notes.txt", PathStyle::Windows);
    if (!drive.Equals("D:notes.txt")) return 3;
    return 0;
}
```

## Rooting and separators

Unix style recognizes `/` as a separator and root. Windows style recognizes
both slash spellings, drive prefixes such as `C:`, and separator-rooted paths.
A Windows drive-relative right component such as `D:notes.txt` replaces the left
even though it is not fully qualified.

Empty components contribute no separator. An inserted separator is `/` for Unix
or `\` for Windows. Existing spelling, repeated separators, and dot segments are
otherwise preserved. This operation does not resolve a current directory,
normalize paths, query the filesystem, or validate filenames.

Use `Join` when a rooted right component should still be concatenated. Use
`GetFullPath` to normalize against an explicit base path. See
[path operations](../Standard/core/IO/PATHS.md).

## Counted input and ownership

Both C-string pairs and `Span<readonly(char)>` pairs are accepted. Counted spans
preserve embedded NUL bytes, while C strings end at their first NUL. The result
owns its bytes independently of both inputs, including when only one component
is retained. Two empty components return an empty string without allocating.

An invalid `PathStyle` throws before constructing a result. Allocation failure
cleans partial output and preserves an existing assignment destination. Input
spans may borrow that destination because result construction finishes before
replacement.
