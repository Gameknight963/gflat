namespace Standard::Memory;

/// Owns one separately allocated object and deletes it on destruction.
/// Move transfers deletion responsibility without relocating the pointee; copying is prohibited.
/// Borrows do not keep the owner alive; replacement or destruction invalidates them.
public struct Unique<T>
{
    private T*? storage;

    public Unique() nothrow { storage = null; }
    /// Adopts one live object suitable for ordinary delete, without allocating.
    /// Do not delete it or adopt it again while this owner is responsible.
    public Unique(T* value) nothrow { storage = value; }

    /// Returns the stored pointer and leaves this owner empty and usable.
    /// Transfers deletion responsibility to the caller without destroying or freeing the object.
    /// An empty owner returns null; existing borrows now rely on whoever owns the released object.
    public T*? Release() nothrow
    {
        T*? result = storage;
        storage = null;
        return result;
    }

    public bool IsEmpty { immutable get nothrow => storage == null; }

    /// Borrows the actual object by mutable reference, throwing when empty.
    /// Available through shallow readonly owners, but not through deep immutable owners.
    /// The borrow transfers no ownership and does not extend the object lifetime.
    public ref T Value
    {
        readonly get
        {
            if (storage == null) throw new Exception("Unique is empty");
            return *storage!;
        }
    }

    /// Borrows the same object through a deep immutable reference, throwing when empty.
    /// Does not copy or allocate, and does not freeze existing mutable aliases.
    /// Keep the responsible owner alive; the borrow transfers no deletion responsibility.
    public ref immutable(T) ValueImmutable
    {
        immutable get
        {
            if (storage == null) throw new Exception("Unique is empty");
            return *storage!;
        }
    }

    ~Unique()
    {
        if (storage != null) delete storage!;
    }
}
