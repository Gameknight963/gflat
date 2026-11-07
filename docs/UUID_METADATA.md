# Inspecting UUID version and variant fields

`Uuid.Version` returns the four version bits, from 0 through 15.
`Uuid.Variant` returns the raw four most significant bits of the ninth canonical
byte, also from 0 through 15. Both are allocation-free `nothrow` properties.

```csharp
using Standard;

int main()
{
    Uuid value = Uuid::Parse("00112233-4455-4677-8899-aabbccddeeff");
    if (value.Version != 4 || value.Variant != 8) return 1;
    if ((value.Variant & 12) != 8) return 2;
    return 0;
}
```

The RFC UUID variant has its two most significant bits set to binary `10`.
Its raw four-bit value can therefore be 8, 9, 10, or 11. Check
`(value.Variant & 12) == 8` when that is the variant you require.
These properties inspect fields without validating a UUID's origin or generation
algorithm. Nil reports zero for both fields, and arbitrary parsed bit patterns
remain accepted.

The property values match [.NET Guid.Version](https://learn.microsoft.com/en-us/dotnet/api/system.guid.version)
and [Guid.Variant](https://learn.microsoft.com/en-us/dotnet/api/system.guid.variant).
