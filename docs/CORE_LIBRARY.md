# Core library

This guide introduces the value and borrowing conventions used by core APIs.
The [API index](STANDARD_LIBRARY_INDEX.md) links to individual facilities.

## Strings and formatting

`String` owns counted bytes and releases its buffer at scope exit. `s"..."` copies
literal bytes into a String. `s$"..."` formats values into one:

```csharp
using Standard::Text;
using Standard;

int main()
{
    String name = s"Ada";
    String message = s$"Hello {name}, score: {42}";
    message.Append("!", 1);
    return 0;
}
```

`Length` counts bytes, including embedded zeros. Indexing and mutation also work
on bytes. Use the [Unicode APIs](UNICODE_TEXT.md) for encoding-aware operations.

Interpolation calls `IFormattable.Format(ITextWriter*)`, or an owning
`IStringConvertable.ToString()` when formatting is unavailable. See
[string conversion](STRING_CONVERSION.md) for custom types and
[String operations](STRINGS.md) for capacity, editing, and borrowed views.

### Resizing and copying strings

Ordinary String assignment copies its bytes. `move text` transfers its buffer and
leaves the source uninitialized. `Resize` changes the initialized byte length,
while `Reserve` changes capacity. See [String storage](STRINGS.md).

## Integer parsing

Use `TryParseInt` when invalid input is an ordinary branch:

```csharp
using Standard::Text;

int value = 0;
if (TryParseInt("-42", &value)) {
    // value is -42.
}
```

It accepts a complete signed decimal integer. Invalid input returns false and
sets the output to zero. Use `ParseInt` to report invalid input with an exception:

```csharp
using Standard::Text;

int value = ParseInt("42");
```

Both APIs accept C strings and counted bytes. See
[integer parsing](INTEGER_PARSING.md) for the other widths and input rules.

## Integer sequences

`IntRange(start, count)` describes consecutive integers. Filtering and slicing are
lazy, and each enumeration starts from the captured range:

```csharp
using Standard;
using Standard::Queries;

int main()
{
    FilteredIntRange values = new IntRange(0, 20).Where((int n) => n % 2 == 0).Skip(1).Take(3);
    int total = 0;
    foreach (int n in values) total += n; // 2 + 4 + 6
    return total;
}
```

`IntRange.Count` returns its known length. `FilteredIntRange.Count()` enumerates
the matches. Both provide `Any()` and a `long`-returning `Sum()`. Negative counts
and ranges extending beyond `int` throw.

Call `AsEnumerable()` for the [general query operators](QUERIES.md). Use
[`Repeated<T>`](REPEATED_VALUES.md) for repeated copies of one captured value.

## Vectors

`Vector<T>` owns contiguous elements. Pass a fresh value, a copyable local, or an
explicitly moved owner to `Add`:

```csharp
using Standard::Text;
using Standard;
using Standard::Collections;

int main()
{
    Vector<String> words = new Vector<String>();
    words.Add(s"first");
    String second = s"second";
    words.Add(move second); // second is now uninitialized.
    second = s"new value";
    words[0].Append("!", 1);
    words[1] = s"replacement";
    String last = words.Pop();
    return 0;
}
```

`Pop` transfers the last element out. `Clear` destroys all elements and retains
capacity. Growth relocates elements and invalidates pointers, spans, and
iterators into the buffer. Allocation failure preserves the vector's existing
contents. An argument consumed by a failed insertion still receives normal
cleanup.

Iteration copies values into the loop variable. For noncopyable elements, use
indexed access or the iterator's `ref Current`. Keep the vector alive and
structurally unchanged while using its borrowed storage.

See [collection operations](../Standard/core/Collections/OWNING_COLLECTIONS.md)
for insertion, removal, capacity management, and failures.

## Spans and readonly views

`Span<T>` stores a pointer and element count. Slicing borrows a subrange:

```csharp
using Standard;
using Standard::Memory;

int main()
{
    int[3] values = [10, 20, 30];
    Span<int> items = new Span<int>(values, 3);
    Span<int> tail = items.Slice(1);
    tail[0] = 42;
    Span<readonly(int)> view = items.AsReadOnly();
    if (view[1] != 42) return 1;
    return 0;
}
```

| View | Access |
| --- | --- |
| `Span<T>` | Mutable elements |
| `Span<readonly(T)>` | Readonly elements |
| `Span<immutable(T)>` | Deeply protected elements and reachable storage |

`AsSpan`, `AsReadOnly`, and `AsImmutable` create these views from a vector.
Indexing returns a reference and checks bounds. `Data` exposes the underlying
nullable pointer for direct access.

Keep the storage owner alive while using a span. Mutations through another alias
remain visible, and operations that relocate the owner invalidate its views.
A string's byte view is `new Span<readonly(char)>(text.Data, text.Length)`.

## Owning an exception message

`Exception` borrows its message. A derived exception can own dynamic text and
return it from `GetMessage()`:

```csharp
using Standard;
using Standard::Text;

class MessageError : Exception
{
    private String text;

    public MessageError(readonly(char)* message) { text = String::From(message); }
    public immutable override readonly(char)* GetMessage() => text.Data;
}

void Fail()
{
    String local = String::From("owned message");
    throw new MessageError(local.Data);
}

int main()
{
    try { Fail(); }
    catch (ref Exception error)
    {
        if (CString::Equals(error.GetMessage(), "owned message")) return 0;
        return 1;
    }
    return 2;
}
```

Each copied exception owns a separate copy of `text`. The returned message pointer
is valid while that exception lives. Leave the inherited `message` field at its
static default, because caching `text.Data` there would leave copies pointing
into another instance's buffer.

The debugger currently reads that base field directly. This example therefore
has an empty debugger message despite its overridden getter.

## Automatically included definitions

Standalone compilation adds base types and runtime contracts. Explicit source
inputs with matching Standard-relative paths take precedence over bundled copies.
Project builds use their evaluated source graph and include Standard by default.

See [source selection](PROJECTS.md#standard-library-selection) for `NoStdLib` and
[freestanding builds](FREESTANDING_CORE.md) for custom startup and native linking.

## Type inspection

`reflect(T)` inspects a type at compile time. `typeof(expression)` inspects an
expression's type without evaluating the expression. Reflection provides field
lists and type information for generic code generation. See the
[language rules](LANGUAGE_SPEC.md#static-type-metadata) and the
[field formatter example](../examples/reflection/README.md).

`Describe<T>()` returns an immutable runtime descriptor with static lifetime.
Follow `ElementType` for pointer layers, `Fields` for declared instance fields,
and `ParameterTypes` for function parameters. For example,
`Describe<int**>().ElementType!.ElementType!.Size` is four.

The descriptors are ordinary Standard types. Include `core/TypeInfo.gf`,
`core/FieldInfo.gf`, and `core/type_descriptions.gf` when selecting sources
manually. See [runtime type descriptions](TYPE_DESCRIPTIONS.md).
