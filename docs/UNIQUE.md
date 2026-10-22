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
Removing or clearing entries destroys those objects; `Pop()` transfers an owner
to its caller instead of destroying its pointee.

Borrows never keep the owner alive. Destroying/reassigning the owner invalidates
all pointers and references to its object. Moving the owner preserves the object's
address, but its lifetime is then controlled by the destination owner. Keep that
owner alive while using the borrow. Raw-pointer aliases are not tracked; this is
an explicit ownership convenience, not a memory-safety guarantee.

Include `core/Unique.gf` with the ordinary exception and allocation runtime
contracts, or use the complete standard library. No reset, release, shared-owner,
or custom-deleter policy API is provided.
