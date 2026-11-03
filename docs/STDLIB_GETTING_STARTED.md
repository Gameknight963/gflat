# Getting started with the standard library

A G♭ project includes Standard by default. Import namespaces for the APIs
you use. For example, Standard::Text contains Strings and parsing, while
Standard::Memory contains spans. See the [namespace layout](../Standard/README.md#namespaces-and-files)
and [project setup](PROJECTS.md).

## Parse some text and print a result

This program prints `Sum: 33`:

```csharp
using Standard::Text;
using Standard;
using Standard::Memory;

int main()
{
    String input = s"12,-3,24";
    extralong total = (extralong)0;
    foreach (Span<readonly(char)> part in input.Split(','))
    {
        Span<readonly(char)> token = part.Trim();
        if (token.IsEmpty)
        {
            Console::Error.WriteLine("Expected an integer");
            return 2;
        }
        long value = 0l;
        if (!token.TryParseLong(&value))
        {
            Console::Error.WriteLine("Invalid signed 64-bit integer");
            return 2;
        }
        total += (extralong)value;
    }
    String summary = s$"Sum: {total}";
    Console::WriteLine(ref summary);
    Console::Flush();
    return 0;
}
```

`s"..."` creates a String, and `s$"..."` formats values into one. Split yields
borrowed byte spans. Trim removes ASCII whitespace. TryParseLong validates a
whole token and returns false on failure. The explicit empty check provides a
more specific error message. See [integer parsing](INTEGER_PARSING.md) for syntax.

## Owners and borrowed views

Keep input alive while using its split parts. Its cleanup releases the owned
bytes at scope exit.

| Operation | Ownership effect |
| --- | --- |
| Copy a String | Creates independent text storage |
| Move a value | Transfers it and consumes that local until reinitialized |
| Borrow through ref, pointer, or Span | Uses the existing value/storage |

Vector and Unique are move-only owners. [Unique](UNIQUE.md) manages one manual
allocation. For collection views, AsReadOnly protects inline element state.
AsImmutable also protects reachable pointees. See
[readonly and immutable](LANGUAGE_SPEC.md#readonly-and-immutable-views).

## Console text and the next step

| Console call | Input |
| --- | --- |
| `WriteLine("hello")` | C string |
| `WriteLine(data, length)` | Counted bytes |
| `WriteLine(ref summary)` | String contents |

Try [number statistics](../examples/number-stats/README.md) for streamed input,
or browse the [library index](../Standard/README.md).
