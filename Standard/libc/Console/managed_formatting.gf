using Standard;

namespace Console;

// Copy the managed reference while keeping its collector and pointee type.
public void Write<T : IFormattable, C : IGarbageCollector>(managed<immutable(T), C> value)
{
    value.Format(Out);
}

public void WriteLine<T : IFormattable, C : IGarbageCollector>(managed<immutable(T), C> value)
{
    value.Format(Out);
    WriteLine();
}
