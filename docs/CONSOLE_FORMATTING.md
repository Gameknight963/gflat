# Print values

Console Write and WriteLine accept primitive values:

```csharp
int main()
{
    Console::WriteLine(42);
    Console::WriteLine(true);
    Console::WriteLine(1.5);
    return 0;
}
```

Integers use decimal digits, Booleans use `true` or `false`, and char writes its
byte value. Floating values use Standard's floating formatter and the current C
locale.

Use `ref` for IFormattable values:

```csharp
using Standard::Text;

struct Label : IFormattable
{
    public immutable void Format(ITextWriter* output)
    {
        output.Write("label", 5);
    }
}

int main()
{
    Label label = new();
    Console::WriteLine(ref label);
    String message = s"hello";
    Console::WriteLine(ref message);
    return 0;
}
```

The ref overload borrows the value during formatting. WriteLine adds its newline
after Format succeeds. A formatter or output error can leave partial text.

[Managed objects](CONSOLE_MANAGED_FORMATTING.md) can be passed directly, including
temporary and returned references.

Selective builds use `Console/value_formatting.gf` with the primitive
formatter and stdout writer. Floating and 128-bit output live in
`floating_formatting.gf` and `extra_long_formatting.gf`, alongside
their respective formatters.
