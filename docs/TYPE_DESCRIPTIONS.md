# Runtime type descriptions

`Describe<T>()` returns a pointer to immutable type data with static lifetime. Follow its links to inspect pointer layers, fields, bases, and function parameters at runtime.

```csharp
using Standard;

struct Node
{
    public int Id;
    public Node*? Next;
}

int main()
{
    immutable(TypeInfo)* node = Describe<Node>();
    immutable(FieldInfo)* fields = node.Fields!;
    immutable(TypeInfo)* next = fields[1].Type;
    if (next.ElementType != node) return 1;
    return 0;
}
```

Descriptors are shared by type. Repeated calls and ordinary aliases return the same address. Recursive types link back to existing descriptors, so a `Node*` field leads to the same `Node` descriptor as `Describe<Node>()`.

## Names and layout

- `Name` contains the qualified source name
- `Kind` identifies the type category through `TypeKind`
- `HasLayout` indicates whether the type has a value layout
- `Size` and `Alignment` count bytes. Both are zero when `HasLayout` is false, including `void`, interfaces, and function signatures
- `HasDestructor` records whether the value has a destructor

An interface pointer has its own layout. Inspect `Describe<IExample*>()` for that layout and `Describe<IExample>()` for the interface itself.

## Type layers

`ElementType` links a pointer, managed reference, fixed buffer, or `ref` type to its element. Other kinds use null. Each layer keeps its own `IsReadOnly`, `IsImmutable`, and `IsNullable` flags.

For example, `Describe<int**>().ElementType` describes `int*`, whose element describes `int`. `readonly(int*)` and `readonly(int)*` put `IsReadOnly` on different layers.

- `IsPointer` covers raw pointers and function pointers
- `IsManaged` identifies managed references. Their `CollectorType` links to the selected collector's descriptor
- Fixed buffers set `HasFixedLength` and store their element count in `Length`

## Fields and bases

`Fields` points to `FieldCount` entries in declaration order. Each `FieldInfo` contains its name, type, byte offset, and public/private flags.

The table contains the type's declared instance fields, including private fields and auto-property backing fields. Computed properties do not add storage. For inherited fields, follow `BaseType` and inspect that descriptor's table. Use `reflect(T).StaticFields` to inspect static fields and constants at compile time.

Offsets are relative to the start of a value. Metadata describing a private field does not grant source-level access to it. Using an offset for raw memory access carries the same responsibilities as other pointer operations.

## Function signatures

Function pointers and function signature types provide:

- `ReturnType`, pointing to the return type descriptor
- `ParameterCount`, giving the number of parameters
- `ParameterTypes`, pointing to an array of descriptor pointers

Read parameter `index` with `info.ParameterTypes![index]`. Zero parameters use a null table and a count of zero. Function signature types have no value layout. Function pointers do.

## Building descriptors

`Describe<T>()`, `TypeInfo`, and `FieldInfo` are ordinary Standard library declarations in `core/type_descriptions.gf`, `core/TypeInfo.gf`, and `core/FieldInfo.gf`.

The library uses compile-time reflection to fill constant records and tables in a generic static holder. Addresses connect those records into a graph. The resulting records can be read at runtime.

Use `reflect(T)` for compile-time type inspection and `typeof(expression)` to obtain the reflection of an expression's type. These language operations work without the Standard library.
