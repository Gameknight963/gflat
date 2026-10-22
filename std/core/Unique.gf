namespace std;

// Owns one individually allocated object. Raw-pointer construction transfers its
// deletion responsibility; the caller must not adopt the same object twice.
public struct Unique<T>
{
    private T*? storage;

    public Unique() nothrow { storage = null; }
    public Unique(T* value) nothrow { storage = value; }

    public bool IsEmpty { immutable get nothrow => storage == null; }

    public ref T Value
    {
        readonly get
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
