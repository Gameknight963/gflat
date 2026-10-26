# Getting started with the standard library

A normal gflat project includes the standard library. Add `using std;` to use its
types and extension methods; [project setup](PROJECTS.md) covers creating and
building a `.gfproj`.

## Parse some text and print a result

This complete program prints `Sum: 33`:

```csharp
using std::Text;
using std; using std::Memory;

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

`s"..."` constructs an owning `String`; `s$"..."` formats values into one.
Here `long` holds each parsed signed 64-bit integer, and `extralong` holds the
exact sum of the three values. `Trim()` removes ASCII whitespace from a borrowed
view. The counted parser consumes the whole remaining token and reports failure
without throwing. The span overload uses its exact byte count and also accepts
an empty span, reporting failure without dereferencing its data pointer. The
separate empty check here gives a more specific error message. See
[integer parsing](INTEGER_PARSING.md) for the accepted
syntax and throwing alternatives.

## Owners and borrowed views

`input` owns its bytes. `Split`, `Trim`, and each `Span<readonly(char)>` carry
borrowed pointer-and-length metadata. Copying these views or iterating the parts
does not clone the String. Keep `input` alive and unchanged while using the parts;
its automatic cleanup releases its allocation when it leaves scope.
See [text splitting and trimming](TEXT_PARTS.md) and [String views](STRINGS.md).

Values use their type's normal copying rules. Copying a `String` invokes its copy
constructor and creates independent text storage. `move value` transfers a value
and consumes that local: initialize it again before using it. Owning types such
as `Vector<T>` and `Unique<T>` prohibit copying; use `move` to transfer them.
[Unique](UNIQUE.md) is an optional owner for one manually allocated object;
`ref` borrows an existing value rather than copying it. Borrowed pointers, spans,
and references require the programmer to keep their backing owner alive.

`AsReadOnly()` protects each element's inline data while allowing changes through
its pointer and managed-reference fields. `AsImmutable()` also protects those
targets through the returned view. For character spans, both prevent character writes.
Other mutable aliases can still change the backing storage. See
[readonly and immutable views](LANGUAGE_SPEC.md#readonly-and-immutable-views).

## Console text and the next step

`Console::WriteLine("hello")` accepts a borrowed null-terminated C string.
`Console::WriteLine(data, length)` accepts counted bytes, including embedded
zeros. `Console::WriteLine(ref summary)` borrows an owning String's counted bytes
without copying its allocation. [Console](../std/libc/README.md#console) also provides caller-buffer
and owning-String input methods.

For a complete streaming program using a reusable input buffer, see
[number statistics](../examples/number-stats/README.md). The
[standard-library index](../std/README.md) links the collection, file, Unicode,
JSON, and formatting APIs as you need them.
