using Standard::IO;

namespace Console;

private reader_detail::StandardReader inputReader = new reader_detail::StandardReader();

// Borrows the process-lifetime stdin reader. The pointer must not be deleted or explicitly destroyed.
public IByteReader* In { get nothrow => &inputReader; }

namespace reader_detail
{
    private struct StandardReader : IByteReader
    {
        // Resolve the current stream through Console each time, without cached state.
        public nuint Read(char* data, nuint count) => Console::Read(data, count);
    }
}
