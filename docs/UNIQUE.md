# Unique ownership

`std::Unique<T>` owns one separately allocated object and deletes it when its owner
is destroyed. It has no copy constructor. Move the owner to transfer its deletion
responsibility, without moving the pointed-to object or allocating another buffer.

```csharp
using std;
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

Default construction is empty. `IsEmpty` has an `immutable` getter. `Value` returns
`ref T` and throws `Exception` on an empty owner. Its `readonly` getter cannot
replace the owner's pointer, but permits changing the pointee through a shallow
readonly owner. An immutable owner cannot call that getter to obtain mutable
access. Passing `ref owner.Value` to a function edits the actual object rather
than copying it. `&owner.Value` explicitly borrows its address when needed.

`ValueImmutable` returns `ref immutable(T)` and is available through mutable,
shallow readonly, and immutable owners. It borrows the same object without copying
or allocating, including when `T` is noncopyable. Its deep view protects inline
fields and storage reached through raw or managed pointers, and permits only
immutable instance methods. It throws the same `Exception("Unique is empty")`
for an empty owner. Existing mutable aliases are not frozen. The borrow follows
the same lifetime and invalidation rules as `Value`; it does not keep an owner
alive or transfer deletion responsibility.

`Unique(T* pointer)` adopts a non-null raw pointer; construction itself is `nothrow`
and does not allocate. After adoption, the caller must not delete the pointer or
adopt it into another owner. It must identify one live object for which ordinary
`delete` is valid: typically an individual `new*` allocation. Stack, placement,
interior, managed, and array-storage pointers do not satisfy that contract.
This wrapper does not infer ownership from arbitrary pointer provenance.

The destructor uses ordinary `delete`, including dynamic class destruction.
`Unique<Base>` can therefore own a derived class through its base pointer.
Noncopyable pointee types work: accessing `Value` does not copy them. Interface
types themselves have no inline value layout, so `Unique<IInterface>` is not
supported by this ref-value API and is rejected by normal generic checking.
Use a concrete class or class base as the owned type.

Moving consumes the source variable under the language's normal rules; it does
not leave an empty usable owner behind. Assigning a fresh owner into an existing
one destroys its previous object. Scope exit and exception cleanup destroy the
remaining owner exactly once. The representation is one nullable raw pointer;
there is no collector, shared counter, or per-owner allocation.

`Vector<Unique<Widget>>` can store fresh owners or consume named ones with `move`.
Growth relocates the owner values while their heap objects keep their addresses.
`Pop()`, `RemoveAt(index)`, and `SwapRemoveAt(index)` transfer the removed owner
to their caller instead of destroying its pointee. Discarding that returned owner
destroys it normally. `Clear`, `Truncate`, `RemoveRange`, and `RemoveAll` destroy
the removed owners and their objects.

Borrows never keep the owner alive. Destroying/reassigning the owner invalidates
all pointers and references to its object. Moving the owner preserves the object's
address, but its lifetime is then controlled by the destination owner. Keep that
owner alive while using the borrow. Raw-pointer aliases are not tracked; this is
an explicit ownership convenience, not a memory-safety guarantee.

Include `core/Unique.gf` with the ordinary exception and allocation runtime
contracts, or use the complete standard library. No reset, shared-owner, or
custom-deleter policy API is provided.

## Releasing a raw pointer

`Release()` returns the stored `T*?` and empties the owner. It is a mutable,
`nothrow` operation that does not allocate, free storage, or destroy the pointee.
An empty owner returns null, including repeated releases. The owner remains
usable: `IsEmpty` is true and its later destruction does nothing. Readonly and
immutable owners cannot call `Release()`.

The caller now owns the deletion responsibility. It can pass the pointer to an
API that explicitly takes ownership, delete it, or construct a new Unique from
its non-null value. Merely borrowing the released address does not dispose it;
letting it go unowned leaks. Existing pointers and references keep the same object
address, but its lifetime is controlled by whoever takes responsibility next.
Releasing a base pointer retains ordinary dynamic-class destruction behavior.

```csharp
using std;

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

Releasing does not provide automatic cleanup for the interval before a new owner
adopts the pointer. Re-adopt before calling potentially throwing code when that
code does not itself take ownership. The new owner then handles exception cleanup
normally. `Release` does not reset the owner to another pointer or change its
copy/move rules.
