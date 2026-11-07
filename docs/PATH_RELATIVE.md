# Computing a relative path

`Paths::GetRelativePath(relativeTo, path, style)` returns an owning String with
a route from the first directory to the target path. Both inputs must be fully
qualified. The selected Unix or Windows style applies on every host, without
filesystem queries or current-directory access.

```csharp
using Standard::IO;
using Standard::Text;

int main()
{
    String relative = Paths::GetRelativePath("/projects/app", "/projects/assets/icon.png", PathStyle::Unix);
    if (!relative.Equals("../assets/icon.png")) return 1;
    String same = Paths::GetRelativePath("/projects/app/", "/projects/app", PathStyle::Unix);
    if (!same.Equals(".")) return 2;
    String otherDrive = Paths::GetRelativePath("C:/projects/app", "D:/assets/icon.png", PathStyle::Windows);
    if (!otherDrive.Equals("D:\\assets\\icon.png")) return 3;
    return 0;
}
```

The first path is always treated as a directory. Inputs are normalized using the
existing [full-path rules](../Standard/core/IO/PATHS.md#resolving-a-full-path): dot
segments are resolved, repeated separators are collapsed, and Windows separators
become backslashes. Comparisons use whole components, so `/a` is not a parent
component of `/ab`.

## Roots, comparison, and trailing separators

- Equivalent paths return `.` even if only one has a trailing separator
- A target below the first directory returns its remaining components
- Other targets on the same root prepend the required `..` components
- Different drive or UNC-share roots return the normalized absolute target
- A remaining target suffix preserves its trailing separator. A route consisting
  only of parent components, such as `..`, omits a redundant trailing separator

Unix-style comparison matches bytes exactly. Windows-style comparison ignores
ASCII letter case in roots and components. Non-ASCII bytes compare exactly,
so Unicode case variants can produce extra parent components or an absolute
result instead of a shorter route. This byte comparison does not inspect the
filesystem's case-sensitivity settings.

## Resolve relative inputs explicitly

Use `GetFullPath` with a chosen base before computing the difference:

```csharp
using Standard::IO;
using Standard::Text;

int main()
{
    String directory = Paths::GetFullPath("app", "/projects", PathStyle::Unix);
    String target = Paths::GetFullPath("assets/icon.png", "/projects", PathStyle::Unix);
    String relative = Paths::GetRelativePath(directory.Data, target.Data, PathStyle::Unix);
    if (!relative.Equals("../assets/icon.png")) return 1;
    return 0;
}
```

## Counted inputs and errors

The `Span<readonly(char)>` overload uses its full counted extent, including zero
bytes. C-string overloads stop at the first zero. Results own their storage and
leave both inputs unchanged. Counted path data is lexical data and need not be
a usable filesystem name.

Invalid styles, empty or relative inputs, incomplete UNC roots, and Windows
device paths throw `Exception`. Allocation failures throw `BadAllocException`.
A failed assignment leaves an existing String unchanged and releases intermediate
normalization buffers.

Selective builds load `core/IO/PathStyle.gf`, `core/IO/Paths.gf`, and the usual
String and Span dependencies. No native IO bridge is needed.
