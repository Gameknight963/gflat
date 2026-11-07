# Checking for a path extension

`Paths.HasExtension(path, style)` checks the final component without allocating.
Both C-string and `Span<readonly(char)>` overloads are `nothrow`. The style is
explicit and works the same way on every host.

```csharp
using Standard::IO;

int main()
{
    if (!Paths::HasExtension("assets/settings.json", PathStyle::Unix)) return 1;
    if (Paths::HasExtension("assets.json/settings", PathStyle::Unix)) return 2;
    if (Paths::HasExtension("settings.json.", PathStyle::Unix)) return 3;
    if (!Paths::HasExtension(".config", PathStyle::Unix)) return 4;
    return 0;
}
```

The last dot must have at least one byte after it in the final component.
An empty path, a path ending in a directory separator, and a final component
ending in a dot return false. A leading dot counts, so `.config` has an extension.
Dots in directory names do not count. Windows style recognizes both slash and
backslash as separators. Unix style treats backslash as an ordinary byte.

This is a lexical test. It does not access the filesystem or validate a filename.
Its result agrees with whether `Paths.GetExtension` returns a nonempty result.
C strings end at the first zero byte. Spans inspect their entire counted range,
including embedded zero bytes.
