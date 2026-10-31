# Unique ownership

`Standard::Memory::Unique<T>` deletes one allocated object when its owner leaves
scope. It is move-only:

```csharp
using Standard;
using Standard::Memory;

class Widget {
    public int Count;
    public Widget(int count) { Count = count; }
}
void Increment(ref int value) { value++; }

int main() {
    Unique<Widget> first = new Unique<Widget>(new* Widget(42));
    Increment(ref first.Value.Count);
    Unique<Widget> second = move first;
    return second.Value.Count - 43;
}
```

| Member | Effect |
| --- | --- |
| Default constructor | Empty owner |
| `Unique(T* pointer)` | Adopts responsibility for deleting one live object |
| `IsEmpty` | Whether the owner has an object |
| `Value` | Borrows the object as `ref T` |
| `ValueImmutable` | Borrows as `ref immutable(T)` |

Adopt a pointer for which ordinary `delete` is valid, usually from `new*`.
Delete responsibility belongs to this owner until moved or released. `Value`
and `ValueImmutable` throw `Exception("Unique is empty")` on an empty owner.

The `Value` getter accepts shallow readonly owners. `ValueImmutable` also accepts
immutable owners. Other mutable aliases can still modify the same object.

Moving consumes the source variable and transfers the allocation's lifetime to
the destination. Existing pointee addresses stay the same. Replacing or destroying
the responsible owner ends that object's lifetime and invalidates its borrows.

`Unique<Base>` can own a derived class and uses dynamic class destruction.
`T` needs a value layout, so use a concrete type or class base. Collections such
as `Vector<Unique<T>>` can relocate owners while their pointees keep their addresses.

## Releasing a raw pointer

`Release()` returns `T*?` and leaves an empty, usable owner. An already empty
owner returns null. The caller now owns deletion responsibility.

```csharp
using Standard;
using Standard::Memory;

class Widget
{
    public int Value;
    public Widget(int value) { Value = value; }
}

int main()
{
    Widget*? raw = null;
    {
        Unique<Widget> owner = new(new* Widget(42));
        raw = owner.Release();
        // This empty owner can leave scope without deleting the released object.
    }
    Unique<Widget> receiver = new(raw!); // Explicitly take deletion responsibility.
    return receiver.Value.Value - 42;
}
```

Re-adopt or delete the result before calling potentially throwing code unless
that code takes ownership. An unowned interval has no automatic cleanup.

Source: `core/Memory/Unique.gf`
