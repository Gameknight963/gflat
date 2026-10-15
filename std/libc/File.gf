namespace std;

// libc FILE
public alias FileHandle = void*?;

// Binary byte stream. 
public struct File
{
    private FileHandle handle;

    public File() { handle = null; }

    private File(readonly(char*) path, readonly(char*) mode)
    {
        handle = file_detail::fopen(path, mode);
        if (handle == null) throw new IOException("File open failed");
    }

    public static File OpenRead(readonly(char*) path) => new File(path, "rb");
    public static File OpenWrite(readonly(char*) path) => new File(path, "wb");
    public static File OpenAppend(readonly(char*) path) => new File(path, "ab");
    public bool IsOpen { readonly get => handle != null; }

    public nuint Read(char* data, nuint count)
    {
        if (handle == null) throw new IOException("File is closed");
        if (count == (nuint)0) return 0;
        nuint read = file_detail::fread(data, 1, count, handle!);
        if (read < count && file_detail::ferror(handle!) != 0)
            throw new IOException("File read failed");
        return read;
    }

    public void Write(readonly(char*) data, nuint count)
    {
        if (handle == null) throw new IOException("File is closed");
        if (count == (nuint)0) return;
        if (file_detail::fwrite(data, 1, count, handle!) != count)
            throw new IOException("File write failed");
    }

    public void Write(readonly(char*) text)
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        this.Write(text, count);
    }

    public void Flush()
    {
        if (handle == null) throw new IOException("File is closed");
        if (file_detail::fflush(handle!) != 0) throw new IOException("File flush failed");
    }

    // fclose consumes the stream even when it reports failure. Never retry it.
    public void Close()
    {
        if (handle == null) return;
        FileHandle stream = handle!;
        handle = null;
        if (file_detail::fclose(stream!) != 0) throw new IOException("File close failed");
    }

    ~File()
    {
        if (handle != null) file_detail::fclose(handle!);
    }
}

namespace file_detail
{
    private extern FileHandle fopen(readonly(char*) path, readonly(char*) mode);
    private extern int fclose(FileHandle stream);
    private extern nuint fread(char* data, nuint size, nuint count, FileHandle stream);
    private extern nuint fwrite(readonly(char*) data, nuint size, nuint count, FileHandle stream);
    private extern int fflush(FileHandle stream);
    private extern int ferror(FileHandle stream);
}
