namespace std;

// libc FILE
public alias FileHandle = void*?;

// Binary byte stream. 
public struct File : ITextWriter
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

    // libc owns temporary naming/location; fclose removes the temporary file.
    public static File OpenTemporary()
    {
        File result = new File();
        result.handle = file_detail::tmpfile();
        if (result.handle == null) throw new IOException("Temporary file creation failed");
        return move result;
    }
    public static void Remove(readonly(char*) path)
    {
        if (file_detail::remove(path) != 0) throw new IOException("File removal failed");
    }

    public static void Rename(readonly(char*) source, readonly(char*) destination)
    {
        if (file_detail::rename(source, destination) != 0) throw new IOException("File rename failed");
    }
    public bool IsOpen { readonly get => handle != null; }
    // Observed stream status, not a prediction: only a read attempt detects EOF.
    public bool EndOfFile
    {
        readonly get
        {
            if (handle == null) throw new IOException("File is closed");
            return file_detail::feof(handle!) != 0;
        }
    }

    public void ClearError()
    {
        if (handle == null) throw new IOException("File is closed");
        file_detail::clearerr(handle!);
    }

    public nuint Read(char* data, nuint count)
    {
        if (handle == null) throw new IOException("File is closed");
        if (count == 0) return 0;
        nuint read = file_detail::fread(data, 1, count, handle!);
        if (read < count && file_detail::ferror(handle!) != 0)
            throw new IOException("File read failed");
        return read;
    }

    // Returns 0..255, or -1 at end of file. IO failures still throw.
    public int ReadByte()
    {
        if (handle == null) throw new IOException("File is closed");
        int value = file_detail::fgetc(handle!);
        if (value == -1 && file_detail::ferror(handle!) != 0)
            throw new IOException("File read failed");
        return value;
    }

    // One-byte lookahead without consuming input. EOF is still observed.
    public int PeekByte()
    {
        int value = this.ReadByte();
        if (value == -1) return -1;
        if (file_detail::ungetc(value, handle!) == -1) throw new IOException("File lookahead failed");
        return value;
    }

    // A short read consumes the available prefix before reporting end of stream.
    public void ReadExactly(char* data, nuint count)
    {
        if (this.Read(data, count) != count)
            throw new EndOfStreamException();
    }

    // Both files are borrowed. Copy begins at the current source position.
    public nuint CopyTo(File* destination, char* buffer, nuint capacity)
    {
        if (handle == null || !destination.IsOpen) throw new IOException("File is closed");
        return this.CopyToWriter(destination, buffer, capacity);
    }

    // Streams counted bytes to any borrowed writer without an intermediate String.
    public nuint CopyTo(ITextWriter* destination, char* buffer, nuint capacity)
        => this.CopyToWriter(destination, buffer, capacity);

    private nuint CopyToWriter(ITextWriter* destination, char* buffer, nuint capacity)
    {
        if (handle == null) throw new IOException("File is closed");
        ITextWriter* self = this;
        if (destination == self) throw new IOException("Cannot copy a file to itself");
        if (capacity == 0) throw new IOException("Copy buffer must not be empty");
        nuint total = 0;
        while (true)
        {
            nuint count = this.Read(buffer, capacity);
            if (count == 0) return total;
            if (count > ((nuint)0 - (nuint)1) - total)
                throw new IOException("File copy length overflow");
            destination.Write(buffer, count);
            total += count;
        }
    }

    public nuint CopyTo(File* destination)
    {
        char[4096] buffer;
        return this.CopyTo(destination, buffer, 4096);
    }

    public nuint CopyTo(ITextWriter* destination)
    {
        char[4096] buffer;
        return this.CopyToWriter(destination, buffer, 4096);
    }

    public void Write(readonly(char)* data, nuint count)
    {
        if (handle == null) throw new IOException("File is closed");
        if (count == 0) return;
        if (file_detail::fwrite(data, 1, count, handle!) != count)
            throw new IOException("File write failed");
    }

    public void Write(readonly(char*) text)
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        this.Write(text, count);
    }

    public void WriteByte(char value) { this.Write(&value, 1); }

    public void Flush()
    {
        if (handle == null) throw new IOException("File is closed");
        if (file_detail::fflush(handle!) != 0) throw new IOException("File flush failed");
    }

    // File streams are binary: the newline is one LF byte on every platform.
    public void WriteLine() { this.Write("\n", 1); }
    public void WriteLine(readonly(char*) text) { this.Write(text); this.WriteLine(); }
    public void WriteLine(readonly(char*) data, nuint count) { this.Write(data, count); this.WriteLine(); }

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
    private extern int feof(readonly(void)* stream);
    private extern void clearerr(FileHandle stream);
    private extern int fgetc(FileHandle stream);
    private extern int ungetc(int value, FileHandle stream);
    private extern FileHandle tmpfile();
    private extern int remove(readonly(char*) path);
    private extern int rename(readonly(char*) source, readonly(char*) destination);
}
