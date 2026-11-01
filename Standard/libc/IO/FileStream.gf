namespace Standard::IO;

// libc FILE
public alias FileHandle = void*?;

// Owns one binary file stream. Default construction is closed.
// Call Close to report final output errors. Destruction closes silently.
// Paths use the host's narrow-character encoding.
public struct FileStream : ITextWriter, IByteReader
{
    private FileHandle handle;

    public FileStream() { handle = null; }

    private FileStream(immutable(char*) path, immutable(char*) mode)
    {
        handle = file_detail::fopen(path, mode);
        if (handle == null) throw new IOException("File open failed");
    }

    // Opens an existing file for binary reading. Opening failure throws IOException.
    public static FileStream OpenRead(immutable(char*) path) => new FileStream(path, "rb");
    // Creates or truncates a binary output file. Opening failure throws IOException.
    public static FileStream OpenWrite(immutable(char*) path) => new FileStream(path, "wb");
    // Opens a binary append stream, creating a missing file. Opening failure throws IOException.
    public static FileStream OpenAppend(immutable(char*) path) => new FileStream(path, "ab");

    // Creates a temporary binary read/write stream whose file is removed on close.
    // Switching read/write direction follows libc positioning rules.
    public static FileStream OpenTemporary()
    {
        FileStream result = new FileStream();
        result.handle = file_detail::tmpfile();
        if (result.handle == null) throw new IOException("Temporary file creation failed");
        return move result;
    }
    public bool IsOpen { immutable get => handle != null; }
    // Reports EOF observed by an earlier read. Throws IOException when closed.
    public bool EndOfFile
    {
        immutable get
        {
            if (handle == null) throw new IOException("File is closed");
            return file_detail::feof(handle!) != 0;
        }
    }

    // Clears sticky EOF and error indicators at the current file position. Throws when closed.
    public void ClearError()
    {
        if (handle == null) throw new IOException("File is closed");
        file_detail::clearerr(handle!);
    }

    // Reads up to count bytes. EOF can return a short result.
    // IO errors can leave a consumed prefix. A closed file rejects even zero count.
    public nuint Read(char* data, nuint count)
    {
        if (handle == null) throw new IOException("File is closed");
        if (count == 0) return 0;
        nuint read = file_detail::fread(data, 1, count, handle!);
        if (read < count && file_detail::ferror(handle!) != 0)
            throw new IOException("File read failed");
        return read;
    }

    // Returns a byte from 0 through 255, or -1 at EOF. IO errors throw IOException.
    public int ReadByte()
    {
        if (handle == null) throw new IOException("File is closed");
        int value = file_detail::fgetc(handle!);
        if (value == -1 && file_detail::ferror(handle!) != 0)
            throw new IOException("File read failed");
        return value;
    }

    // Returns the next byte without consuming it, or -1 at EOF.
    // Reading or pushback failure throws IOException.
    public int PeekByte()
    {
        int value = this.ReadByte();
        if (value == -1) return -1;
        if (file_detail::ungetc(value, handle!) == -1) throw new IOException("File lookahead failed");
        return value;
    }

    // Fills count bytes or throws EndOfStreamException on early EOF.
    // A failure can leave consumed input and a written prefix.
    public void ReadExactly(char* data, nuint count)
    {
        if (this.Read(data, count) != count)
            throw new EndOfStreamException();
    }

    // Discards count bytes sequentially. Early EOF throws EndOfStreamException.
    // Failure leaves already consumed bytes discarded. A closed file rejects even zero count.
    public void SkipExactly(nuint count)
    {
        if (handle == null) throw new IOException("File is closed");
        char[4096] buffer;
        while (count != 0)
        {
            nuint chunk = count;
            if (chunk > 4096) chunk = 4096;
            this.ReadExactly(buffer, chunk);
            count -= chunk;
        }
    }

    // Copies remaining bytes into destination using caller scratch. Both files stay open.
    // Rejects closed files, an empty buffer, or the same FileStream instance before reading.
    // A failure can leave consumed input and partial output.
    public nuint CopyTo(FileStream* destination, char* buffer, nuint capacity)
    {
        if (handle == null || !destination.IsOpen) throw new IOException("File is closed");
        return this.CopyToWriter(destination, buffer, capacity);
    }

    // Copies remaining bytes to a borrowed writer through caller scratch.
    // Sink failure can leave the current chunk consumed and earlier output written.
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

    // Copies remaining bytes using 4096 bytes of stack scratch. Both files stay open.
    public nuint CopyTo(FileStream* destination)
    {
        char[4096] buffer;
        return this.CopyTo(destination, buffer, 4096);
    }

    // Copies remaining bytes through stack scratch, leaving the source open.
    public nuint CopyTo(ITextWriter* destination)
    {
        char[4096] buffer;
        return this.CopyToWriter(destination, buffer, 4096);
    }

    // Writes counted bytes, including zeros. IO errors can leave partial output.
    // A closed file rejects even zero count.
    public void Write(readonly(char)* data, nuint count)
    {
        if (handle == null) throw new IOException("File is closed");
        if (count == 0) return;
        // Buffered libc implementations may return count despite a flush error.
        if (file_detail::fwrite(data, 1, count, handle!) != count || file_detail::ferror(handle!) != 0)
            throw new IOException("File write failed");
    }

    // Writes bytes before the first zero.
    public void Write(immutable(char*) text)
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        this.Write(text, count);
    }

    public void WriteByte(char value) { this.Write(&value, 1); }

    // Flushes buffered output, throwing IOException on failure.
    public void Flush()
    {
        if (handle == null) throw new IOException("File is closed");
        if (file_detail::fflush(handle!) != 0) throw new IOException("File flush failed");
    }

    // Writes one LF byte on every platform.
    public void WriteLine() { this.Write("\n", 1); }
    public void WriteLine(immutable(char*) text) { this.Write(text); this.WriteLine(); }
    public void WriteLine(immutable(char*) data, nuint count) { this.Write(data, count); this.WriteLine(); }

    // Closes the stream and reports IOException on close failure.
    // The FileStream stays closed even on failure. Repeated Close is allowed.
    public void Close()
    {
        if (handle == null) return;
        FileHandle stream = handle!;
        handle = null;
        if (file_detail::fclose(stream!) != 0) throw new IOException("File close failed");
    }

    ~FileStream()
    {
        if (handle != null) file_detail::fclose(handle!);
    }
}

namespace file_detail
{
    private extern FileHandle fopen(immutable(char*) path, immutable(char*) mode);
    private extern int fclose(FileHandle stream);
    private extern nuint fread(char* data, nuint size, nuint count, FileHandle stream);
    private extern nuint fwrite(immutable(char*) data, nuint size, nuint count, FileHandle stream);
    private extern int fflush(FileHandle stream);
    private extern int ferror(FileHandle stream);
    private extern int feof(readonly(void)* stream);
    private extern void clearerr(FileHandle stream);
    private extern int fgetc(FileHandle stream);
    private extern int ungetc(int value, FileHandle stream);
    private extern FileHandle tmpfile();
}
