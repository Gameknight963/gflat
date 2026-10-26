namespace std::Memory;

// Owns one individually allocated object. Raw-pointer construction transfers its
// deletion responsibility; the caller must not adopt the same object twice.
public struct Unique<T>
{
    private T*? storage;

    public Unique() nothrow { storage = null; }
    public Unique(T* value) nothrow { storage = value; }

    // Transfer deletion responsibility to the caller; keep this owner empty and usable.
    public T*? Release() nothrow
    {
        T*? result = storage;
        storage = null;
        return result;
    }

    public bool IsEmpty { immutable get nothrow => storage == null; }

    public ref T Value
    {
        readonly get
        {
            if (storage == null) throw new Exception("Unique is empty");
            return *storage!;
        }
    }

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
