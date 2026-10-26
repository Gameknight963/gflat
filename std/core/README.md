# Core library

See the [standard library index](../README.md) for collections, hashing, parsing,
UTF-8, binary codecs, paths, math, and runnable examples.

Lazy collection queries and their ownership rules are documented in
[Collections](Queries/README.md). The runnable example is
[`examples/queries`](../../examples/queries/main.gf).

Portable facilities use the replaceable `Memory` API. The default
allocator delegates to libc; replacing it does not require changing these sources.
Projects using the standard build rules reference `std/std.gfproj` by default. Set `NoStdLib` to `true`
to supply your own library inputs or build std itself.

## Strings and formatting

```csharp
using std;

int main()
{
    String name = s"Ada";
    String message = s$"Hello {name}, score: {42}";
    message.Append("!", 1);
    return 0;
}
```

`String` owns a mutable byte buffer and frees it at scope exit. UTF-8 is a
convention; construction and byte mutations do not validate encoding. `Length` and
`Capacity` count bytes, excluding the trailing null. Indexing operates on bytes
and checks bounds.

- `new String()` creates an empty string without allocating.
- `String::From(cString)` copies null-terminated input.
- `String::From(data, length)` copies exactly that many bytes, including nulls.
  The `s"..."` literal uses this counted form.
- `Data` and `ToCString()` borrow the existing buffer. Do not free it. Mutation,
  movement, or destruction can invalidate the view; mutations can change its text.
- `Reserve`, `Append`, and `AppendLiteral` throw on overflow or allocation failure.
  Allocation failure throws `BadAllocException` and preserves the existing storage
  and contents; overflow remains a separate error.
  Appending from the same string works, including across growth.
- `Clear` retains capacity. `ShrinkToFit()` explicitly reduces capacity to length,
  releasing empty storage; allocation failure preserves the buffer. `Clone` and
  `ToString()` make independent owned copies.
- `Resize(length, fill = '\0')` changes the counted byte length. Growing initializes
  every new byte with `fill`, retaining the existing prefix; shrinking retains
  capacity. The trailing terminator is maintained in either case. A null fill
  creates counted embedded nulls, so `Resize(16)` prepares sixteen initialized
  bytes for a counted codec output, rather than an empty C string. Resize to the
  current length does nothing. Growth overflow or allocation failure preserves
  length, contents, capacity, and storage. Fill is passed by value, so
  `text.Resize(32, text[0])` works even if growth relocates its buffer.
- `String copy = text;` makes an independent owned copy and may throw on allocation failure.
- `move text` transfers the buffer and leaves `text` uninitialized. Assign a new value before using it again.
- `Equals(immutable(String)* other)` compares bytes.
- `Equals(cString)` and `Equals(data, count)` compare borrowed text without an
  intermediate String. The counted overload includes embedded nulls; the C-string
  overload stops at its first null. Comparison is exact and does not allocate.
- `IndexOfAny` and `LastIndexOfAny` search for any byte in a supplied set and return
  `String::NotFound` on a miss. Counted sets can include NUL; C-string sets stop at
  it. See [byte-set searches](../../docs/BYTE_SEARCH.md#string-searches).

Interpolation uses `IFormattable.Format(ITextWriter*)` and
`ITextWriter.Write(readonly(char)*, nuint)`. The writer consumes bytes during the
call; it must not retain the input pointer. Strings write their existing bytes,
including embedded nulls. Primitive `std::Format(value, writer)` overloads use
stack buffers or literal text, without allocating intermediate strings.
With `using std;`, primitive values also support `value.Format(writer)`.
Floating-point overloads live in `../libc/Formatting.gf`.
Explicit `ToString()` extensions collect the same formatting into an owning
`String`. Primitive receivers use value parameters; the generic `IFormattable`
extension borrows existing storage, including noncopyable values. Existing
instance methods keep precedence. See [owning conversions](../../docs/STRING_CONVERSION.md)
for receiver restrictions and selective source dependencies.
Signed 128-bit `extralong` uses `ExtraLongFormatting.gf` together with
`IntegerFormatting.gf`, including for interpolation. It supports the whole
signed range without allocating an intermediate string or requiring a
128-bit division runtime.

Types can instead implement `IStringConvertable`, whose immutable `ToString()`
returns an owning `std::String`. Interpolation keeps that temporary alive while
writing its counted bytes, then destroys it. If a type implements both contracts,
the direct `Format` operation takes precedence. A borrowed `ToCString()` method
is optional and is never selected implicitly for interpolation.

Only C-string consumers stop at the first null; counted operations and interpolation
preserve it. Constructors and factory methods follow ordinary exception propagation.

`Rune` represents a validated Unicode scalar. Formatting and interpolation write
its UTF-8 encoding, while its `Value` property exposes the numeric `uint`.
Its default U+0000 remains a counted byte in an owning string. See
[Unicode scalar values](../../docs/RUNE.md).

## Integer parsing

`TryParseInt(text, &value)` parses a null-terminated decimal integer.
`TryParseInt(data, count, &value)` parses exactly `count` bytes without requiring
a terminator. Both return `bool` and write an `int` through the result pointer:

```csharp
int value = 0;
if (TryParseInt("-42", &value)) {
    // value is -42.
}
```

An optional leading `+` or `-` is accepted. Empty input, whitespace, nondecimal
characters, and values outside the signed 32-bit range return `false` and set
the result to zero. Parsing consumes the entire input and does not allocate,
throw, or call libc. With `using std;`, C strings support
`text.TryParseInt(&value)` and `text.ParseInt()`. A `String` supports the same
calls using its counted bytes, without copying the string.

`ParseInt(text)` and `ParseInt(data, count)` return the parsed `int` directly,
using the same rules. They throw `Exception` on invalid input or overflow:

```csharp
int value = ParseInt("42");
```

## Integer sequences

With `using std;`, `new IntRange(start, count)` describes consecutive integers without
allocating a buffer. `Where` accepts a `bool(int)*` callback; typed noncapturing
lambdas work directly. `Take` and `Skip` return new sequence values:

```csharp
using std; using std::Queries;

int main()
{
    FilteredIntRange values = new IntRange(0, 20).Where((int n) => n % 2 == 0).Skip(1).Take(3);
    int total = 0;
    foreach (int n in values) total += n; // 2 + 4 + 6
    return total;
}
```

Queries are lazy and repeatable: creating or slicing one does not run its
predicate, and each enumeration starts again. Filtering runs as far as requested;
`Take(0)` never calls the predicate. Operation order matters: slicing before
filtering limits input integers, while slicing afterward limits matching integers.
Predicate exceptions propagate normally.

Both sequence types support `Any()` and `Sum()`. `IntRange` exposes its known
length through the `Count` property; `FilteredIntRange.Count()` enumerates to
count matching values. Filtered aggregates enumerate as needed. Sums return
`long` so any valid range fits; negative counts and ranges exceeding `int` throw.
Enumerator `Current` is valid only after a successful `MoveNext()` and before
exhaustion.

These specialized helpers coexist with the [generic query API](Queries/README.md).
Use `IntRange.AsEnumerable()` to access those operators. The range types implement
`IEnumerable<int, IntRange::Enumerator>` and
`IEnumerable<int, FilteredIntRange::Enumerator>`, respectively.
Callbacks can call other functions but cannot capture local variables. The
sequence and iterator values own no heap storage and need no manual disposal.

For repeated copies of one value, use [`Repeated<T>`](../../docs/REPEATED_VALUES.md).
Its cursors own captured copies rather than borrowing the sequence's address;
`AsEnumerable()` exposes the generic query operators.

## Vectors

`Vector<T>` owns contiguous elements. It supports primitives and owning values:

```csharp
using std; using std::Collections;

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

`Add(T)` takes ownership of its argument. Fresh values transfer directly; named
owners copy when their type supports it; use `move` to consume a local instead. `Pop()` transfers the last element out. `Clear()` destroys
all elements while retaining capacity. `Reserve(nuint)` grows storage, and the
destructor releases elements and storage. Copy a vector explicitly element by
element when that is desired; `move vector` transfers its buffer instead.
Allocation failure throws `BadAllocException` without changing the existing
elements, count, capacity, or storage. A failed `Add` still destroys its consumed argument.

Growth relocates each element and frees the old allocation without destroying the
transferred values. No move constructor is required. Noncopyable resource types can
be elements: pass fresh values or `move` a local. `Vector<T>` itself is noncopyable
in this version; moving it transfers the buffer. Types needing a stable address must
use indirection. Growth invalidates pointers into the buffer.
Zero-sized elements are supported, but their addresses are not distinct identities.

`foreach` over a mutable `Vector<T>` or `Span<T>` copies each element into the loop
variable. `String` elements therefore make independent owned copies; noncopyable
resource elements require indexed access instead. Iterators allocate no storage.
Their `ref Current` permits explicit element access after `MoveNext()` succeeds and
throws before the first element or after exhaustion.

`Vector<T>.AsSpan()` borrows a mutable view. `AsReadOnly()` on vectors and spans
returns `Span<readonly(T)>`; use `foreach` over this view for readonly collections.
`AsImmutable()` returns `Span<immutable(T)>` and also protects storage reached
through each element. Declare deeply protected reference or owning element types
explicitly when required, such as `immutable(Node^)` or `immutable(String)`. Mutable collections retain mutable
reference elements.

Views and iterators do not extend the owner's lifetime. Keep the owner alive and
do not grow, clear, remove elements from, or destroy the vector during iteration.
Direct iteration over a temporary vector keeps it alive until the loop finishes;
obtaining a view from a temporary vector does not keep that vector alive.

## Spans and readonly views

`Span<T>` borrows contiguous storage without allocating or freeing it. There is
one span type; use `Span<readonly(T)>` for a view of readonly elements.

```csharp
using std; using std::Memory;

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

- Construction takes a pointer and element count. The caller provides valid,
  initialized elements. The default constructor creates an empty view.
- Lengths and indices use `nuint`. Indexing and slicing check bounds.
- A ref indexer exposes existing elements: it supports owning types, in-place
  mutation, and taking addresses. It does not copy an owner just to access it.
- Access through `readonly(Span<T>)` or `readonly(Vector<T>)` protects immediate
  storage. `immutable(Span<T>)` and `immutable(Vector<T>)` additionally protect
  elements and their reachable storage through immutable reference getters.
- `Span<T>.AsReadOnly()` returns `Span<readonly(T)>`. Use this view when slicing
  through a readonly span; the resulting slice preserves element readonly.
- `AsImmutable()` returns `Span<immutable(T)>` when deep element protection is
  required. The two qualifications are equivalent for primitive scalar elements.
- `Data` is nullable and bypasses bounds checks. Copying a span copies only its
  pointer and length.

The caller must keep storage alive. Bounds checks cannot detect dangling pointers
or incorrect counts. Readonly views observe changes through other mutable views.
A string byte view is `new Span<readonly(char)>(text.Data, text.Length)`.

## Automatically included definitions

Standalone source compilation includes a small set of base types and runtime
contracts automatically. These sources ship beside the compiler and language
server in `Std/`; explicit inputs with matching std-relative paths take precedence
over the bundled copies.

Project builds instead use their evaluated source graph, including the default std
project reference. `NoStdLib` disables that reference and automatic definitions;
the compiler API and standalone CLI offer the same choice. See
[project setup](../../docs/PROJECTS.md#standard-library-selection) for details.
Excluding std does not itself remove the host C runtime from native linking.

## Static type metadata

`typeof(T)` returns `std::TypeInfo`: a constant description of a static type's
name, layout, and reference traits. Its fields are readonly and its name has
static lifetime. It works in generic code and constant expressions; member
reflection and runtime type discovery are not provided yet.

`Kind` distinguishes value and reference shapes. `ElementType` is a nullable
pointer to immutable static metadata for the pointee/element of a raw pointer,
managed reference, array, or reference parameter. It can be followed recursively:
`typeof(int**).ElementType!.ElementType!.Size` is four. Other kinds have a null
element.

Function pointers expose `ReturnType`, `ParameterCount`, and `ParameterTypes`.
The latter points to a contiguous readonly descriptor array; index below the
count. Zero parameters use a null array, and non-functions use null/null/zero.
Void returns have a descriptor with no value layout. Signatures can be followed
recursively, including through nested callbacks, reference parameters, readonly
views, arrays, and nullable/managed pointers:
`typeof(int(int**)*).ParameterTypes![0].ElementType!.ElementType!.Size` is four.
The array and its children have static lifetime and need no allocation or disposal.
Parameter names, defaults, and method modifiers are not part of a function-pointer
type and are not exposed here.

`IsReadOnly` describes the current value layer: `readonly(int)*` has a mutable
pointer and readonly element; `readonly(int*)` has a readonly pointer and mutable
element. `IsImmutable` identifies deep protection at the current layer;
`immutable(int*)` protects both the pointer and its element. Primitive leaves
canonicalize immutable protection to readonly because there is no deeper storage.
`IsNullable` likewise describes only the current layer. `HasLayout` is false for void/interface
elements, with zero size/alignment; pointers to them have their own layouts.
`HasFixedLength` and `Length` describe fixed arrays, with zero length otherwise.
Descriptors do not allocate managed objects or provide dynamic runtime type tests.
