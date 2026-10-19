namespace Console;

private writer_detail::StandardWriter outputWriter = new writer_detail::StandardWriter(false);
private writer_detail::StandardWriter errorWriter = new writer_detail::StandardWriter(true);

// Borrow stable process-lifetime writers. Never delete these pointers.
public ITextWriter* Out { get nothrow => &outputWriter; }
public ITextWriter* Error { get nothrow => &errorWriter; }

public void FlushError()
{
    if (writer_detail::fflush(writer_detail::__gflat_stdio_stderr()) != 0)
        throw new std::IOException("Console error flush failed");
}

namespace writer_detail
{
    private struct StandardWriter : ITextWriter
    {
        private bool error;
        public StandardWriter(bool useError) nothrow { error = useError; }
        public void Write(readonly(char)* text, nuint count)
        {
            if (!error) { Console::Write(text, count); return; }
            if (count == 0) return;
            void* stream = __gflat_stdio_stderr();
            if (fwrite((readonly(void)*)text, 1, count, stream) != count || ferror(stream) != 0)
                throw new std::IOException("Console error write failed");
        }
    }

    private extern void* __gflat_stdio_stderr();
    private extern nuint fwrite(readonly(void)* data, nuint size, nuint count, void* stream);
    private extern int fflush(void* stream);
    private extern int ferror(void* stream);
}
