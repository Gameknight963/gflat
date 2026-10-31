namespace Standard::Memory;

// Move-only owner of one allocated object. Move preserves the object's address.
// Replacement or destruction invalidates borrowed references.
public struct Unique<T>
{
    private T*? storage;

    public Unique() nothrow { storage = null; }
    // Takes ownership of a live object suitable for delete.
    public Unique(T* value) nothrow { storage = value; }

    // Transfers the stored pointer to the caller and leaves this owner empty.
    // An empty owner returns null. The caller takes responsibility for deletion.
    public T*? Release() nothrow
    {
        T*? result = storage;
        storage = null;
        return result;
    }

    public bool IsEmpty { immutable get nothrow => storage == null; }

    // Borrows the object by mutable reference, throwing when empty.
    // Keep the owner alive. Shallow readonly owners permit this access.
    public ref T Value
    {
        readonly get
        {
            if (storage == null) throw new Exception("Unique is empty");
            return *storage!;
        }
    }

    // Borrows an immutable reference, throwing when empty. Keep the owner alive.
    // Other aliases can still change the object.
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
