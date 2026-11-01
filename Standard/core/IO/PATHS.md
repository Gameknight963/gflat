# Lexical paths

`Standard::IO::Paths` operates on path bytes using an explicit PathStyle::Unix or
PathStyle::Windows. The selected style applies on every host.

## Inputs and results

C-string overloads return owning Strings. GetFileName, GetDirectoryName,
GetExtension, GetFileNameWithoutExtension, and GetRoot also accept
`Span<readonly(char)>` and return slices of that input:

```csharp
using Standard::Text;
using Standard::IO;
using Standard::Memory;

String path = s"assets/settings.json";
Span<readonly(char)> name = Paths::GetFileName(path.AsReadOnly(), PathStyle::Unix);
// name borrows "settings.json" from path.
```

A returned slice borrows the original storage and observes edits through aliases.
Keep its owner alive and reacquire the slice after relocation. Empty slices use
null Data and zero Length.

## Components and extensions

| Operation | Result |
| --- | --- |
| `GetFileName` | Final component. A trailing separator produces an empty result |
| `GetDirectoryName` | Containing directory, retaining a root where appropriate. `dir/` gives `dir` |
| `GetExtension` | Last dot and suffix in the filename. `.config` gives `.config`, and `file.` gives empty |
| `GetFileNameWithoutExtension` | Filename with the last dot and suffix removed |

Directory names containing dots leave the filename's extension unaffected.
Roots and paths with no containing directory produce an empty directory result.

### Changing extensions

`ChangeExtension(path, extension, style)` changes the final filename's suffix:

| Input | Replacement | Result |
| --- | --- | --- |
| `dir/file.old` | `txt` | `dir/file.txt` |
| `.config` | `json` | `.json` |
| `file.` | `txt` | `file.txt` |
| `file` | `.` | `file.` |
| `file.old` | Empty | `file` |

A nonempty replacement gets a leading dot when needed. Paths without a filename,
such as roots and trailing-separator paths, remain unchanged. Both C-string and
counted-span pairs return an owning String. Counted inputs preserve embedded zeros.

## Roots and separators

`GetRoot` returns the path's root or drive prefix:

- Unix recognizes `/`
- Windows recognizes drive roots (`C:\`), drive-relative prefixes (`C:`), rooted
  separators, and UNC server/share roots

An existing root separator is retained. EndsInDirectorySeparator checks the final
byte and returns false for an empty path.

`TrimEndingDirectorySeparator` removes one separator beyond the root. `/` stays
`/`, `dir//` becomes `dir/`, and `//` becomes `/`. Windows `C:\` stays `C:\`.
The C-string form returns a String, and the span form returns a borrowed slice.

## Joining and classification

`Join(left, right, style)` joins components, inserting a separator if both
boundaries lack one or removing one duplicated boundary separator. A rooted
right component is still joined to the left. Other separators and dot segments
are preserved. Both C-string and span pairs return a String.

| Test | Unix | Windows |
| --- | --- | --- |
| `IsSeparator` | `/` | `/` or `\` |
| `IsRooted` | Leading separator | Leading separator or drive prefix |
| `IsFullyQualified` | Leading `/` | Two leading separators, or a drive prefix followed by a separator |

`C:child` and `\child` are rooted but depend on a current drive directory or
current drive. The fully-qualified test checks root spelling: `\\` qualifies
although it is an incomplete UNC share. Device prefixes and alternate data streams
follow the same byte rules.

Lexical joining preserves dot segments. Validate directory confinement before
using an untrusted result for filesystem access.

## Resolving a full path

`GetFullPath(path, basePath, style)` returns an owning String. The base must have
an absolute Unix root, a Windows drive root such as `C:\`, or a complete UNC
server/share root.

```csharp
using Standard::IO;
using Standard::Text;

int main()
{
    String path = Paths::GetFullPath("../images/./icon.png", "/project/assets", PathStyle::Unix);
    if (!path.Equals("/project/images/icon.png")) return 1;
    return 0;
}
```

- Relative input resolves against the base directory. Empty input returns the normalized base
- Absolute input uses its own root. The base still requires a supported absolute root
- Windows input beginning with one separator, such as `\images`, uses the base's drive or UNC share root
- Repeated separators collapse. `.` is removed and `..` removes a component, stopping at the root
- Output uses `/` for Unix and `\` for Windows. An explicit trailing separator is retained
- Component bytes and their casing are preserved. Counted-span overloads also preserve embedded zeros

Drive-relative Windows forms such as `C:images`, device paths such as `\\?\C:\`,
incomplete UNC roots, unsupported styles, and nonabsolute bases throw `Exception`.
Allocation failures throw `BadAllocException`. Inputs remain unchanged, and failed
normalization releases its partial result.

The existing `Join`, `IsRooted`, and `IsFullyQualified` operations retain their
lexical rules. Full-path resolution uses the stricter root requirements above.

## Source selection

Selective builds include IO/PathStyle.gf, IO/Paths.gf, String, and Span with its
collection/query contracts. Ordinary Standard projects include them.
