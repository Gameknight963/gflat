# Copying values from maps

`HashMap<K, V>` and `SortedMap<K, V>` provide copying lookup extensions in
`Standard::Collections`. Use them when the result should survive changes to
the map or the map's destruction.

| Method | Result |
| --- | --- |
| `TryGetValue(&key, ref output)` | Copy the stored value into initialized output and return true, or preserve output and return false when absent |
| `GetValueOrDefault(&key, fallback)` | Copy a stored value when present, otherwise return the supplied fallback |
| `GetValueOrDefault(&key)` | Copy a stored value when present, otherwise return `new V()` |

The no-fallback form requires accessible parameterless construction of `V`.
Construction runs only on a miss. For numeric values the result is zero, and
for `String` it is an empty owning string. A user constructor can do additional
work or throw. Use an explicit fallback for types without that constructor.

```csharp
using Standard::Collections;
using Standard::Text;

int main()
{
    HashMap<int, String> names = new();
    names.Add(1, s"Ada");
    int known = 1;
    int missing = 2;
    String name = s"previous";
    if (!names.TryGetValue(&known, ref name) || !name.Equals("Ada")) return 1;
    if (names.TryGetValue(&missing, ref name) || !name.Equals("Ada")) return 2;

    String label = names.GetValueOrDefault(&missing, s"anonymous");
    String empty = names.GetValueOrDefault(&missing);
    names.Clear();
    if (!name.Equals("Ada") || !label.Equals("anonymous") || !empty.IsEmpty) return 3;
    return 0;
}
```

## Ownership and errors

The mapped type must be copyable. Keys are borrowed, so these operations do not
require a key copy. Results use `V`'s ordinary copy semantics. Use `Find`,
`FindReadOnly`, or `FindImmutable` to borrow a value, including a noncopyable
owner.

An explicit fallback is passed by value, even for a successful lookup. Use
`move fallback` to transfer an existing owner. A miss transfers it into the
return value. A hit destroys it after copying the stored value.

`TryGetValue` finishes lookup and constructs the copy before replacing output.
A missing key, lookup error, copy error, or allocation failure during copying
leaves output unchanged. Output can be a mapped value in the same map,
including the found value itself. Assigning that output changes the selected
mapped slot, without changing keys or the map's count and capacity.

Lookups can throw from hashing, equality, or comparison. Copying and default
construction can also throw. These extensions do not insert missing entries
or grow the map. A missing `TryGetValue` does not copy or construct a value.

Freestanding forms take an immutable map pointer first, for example
`TryGetValue(&names, &key, ref output)`. Selective builds include
`Collections/hash_map_lookup.gf` or `Collections/sorted_map_lookup.gf` alongside
the corresponding map source. Keeping copying helpers separate leaves ordinary
map operations available for noncopyable values.
