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
| `Reset()` | Deletes the object and leaves an empty owner |
| `Reset(T*? pointer)` | Adopts a replacement and deletes the previous object |
| `Swap(ref other)` | Exchanges deletion responsibility without moving the objects |

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

## Early cleanup and exchanging owners

Use `Reset()` to end the object's lifetime before the owner leaves scope. Resetting
an empty owner does nothing. The owner remains usable and can receive another
owning value through assignment.

`Reset(pointer)` takes deletion responsibility for a replacement before destroying
the previous object. Passing null empties the owner. Passing its current pointer
leaves it unchanged. If creating the argument fails, the previous object remains
owned because the call has not begun.

`Swap(ref other)` exchanges two owners without allocating, copying, or relocating
their pointees. Borrowed pointers retain their addresses, but the other owner now
controls their lifetime. Swapping an owner with itself leaves it unchanged.
Both operations require mutable owners.

```csharp
using Standard::Memory;

int main()
{
    Unique<int> first = new(new* int(7));
    Unique<int> second = new(new* int(42));
    first.Swap(ref second);
    if (first.Value != 42 || second.Value != 7) return 1;
    second.Reset();
    if (!second.IsEmpty) return 2;
    return 0;
}
```

Source: `core/Memory/Unique.gf`
