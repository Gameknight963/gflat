# Reading and changing the current directory

`Directory::GetCurrentDirectory()` returns an owning String containing the
process's absolute working directory. `Directory::SetCurrentDirectory(path)`
changes it to an existing directory. Relative paths are resolved against the
current working directory.

```csharp
using Standard::IO;
using Standard::Text;

int main()
{
    String original = Directory::GetCurrentDirectory();
    defer { Directory::SetCurrentDirectory(original.Data); }
    Directory::SetCurrentDirectory(".");
    String current = Directory::GetCurrentDirectory();
    if (!current.Equals(original.Data)) return 1;
    return 0;
}
```

The working directory belongs to the process, so changing it affects relative
file operations in every thread. Returned Strings are independent snapshots
and remain unchanged after later directory changes. Keep using explicit absolute
paths when an operation must be independent of this process setting.

Both methods use the platform's narrow C-runtime paths, like the other Directory
operations. On Windows, the active CRT locale controls path encoding. Use the
UTF-8 CRT locale when passing UTF-8 names. Unix paths use native path bytes.
Neither method creates a directory.

## Errors and allocation

An empty path, missing target, nondirectory target, or another OS failure in
`SetCurrentDirectory` throws `IOException`. Failure leaves the current directory
unchanged. Setting the directory does not allocate Standard-owned storage.

`GetCurrentDirectory` grows its own buffer as needed and throws `IOException`
on an OS query error or `BadAllocException` if buffer allocation fails. The query
does not change the working directory. Assignment to an existing String occurs
only after the new owning result is complete, so a failed query preserves the
previous String.

Selective builds load `libc/IO/Directory.gf`, `core/IO/IOException.gf`, and String's
normal core dependencies. Native programs link the existing stdio runtime bridge.
