namespace Standard::IO;

// libc FILE
public alias FileHandle = void*?;

/// Owns one libc binary byte stream. Move transfers it and default construction is closed.
/// Destruction closes without reporting errors. Call Close to check final flushing.
/// Paths are borrowed zero-terminated native bytes, without encoding conversion.
public struct File : ITextWriter, IByteReader
{
    private FileHandle handle;

    public File() { handle = null; }

    private File(immutable(char*) path, immutable(char*) mode)
    {
        handle = file_detail::fopen(path, mode);
        if (handle == null) throw new IOException("File open failed");
    }

    /// Opens an existing file for binary reading, throwing IOException if opening fails.
    public static File OpenRead(immutable(char*) path) => new File(path, "rb");
    /// Creates or truncates a file for binary writing, throwing IOException if opening fails.
    public static File OpenWrite(immutable(char*) path) => new File(path, "wb");
    /// Opens for binary append, creating a missing file. Opening failure throws IOException.
    public static File OpenAppend(immutable(char*) path) => new File(path, "ab");

    /// Creates a libc-managed temporary binary read/write stream.
    /// Closing removes its temporary backing file. Position and direction follow libc rules.
    public static File OpenTemporary()
    {
        File result = new File();
        result.handle = file_detail::tmpfile();
        if (result.handle == null) throw new IOException("Temporary file creation failed");
        return move result;
    }
    public static void Remove(immutable(char*) path)
    {
        if (file_detail::remove(path) != 0) throw new IOException("File removal failed");
    }

    public static void Rename(immutable(char*) source, immutable(char*) destination)
    {
        if (file_detail::rename(source, destination) != 0) throw new IOException("File rename failed");
    }
    public bool IsOpen { immutable get => handle != null; }
    /// Reports the observed EOF indicator. A read attempt must first detect EOF.
    /// Does not predict the next read. Throws IOException when closed.
    public bool EndOfFile
    {
        immutable get
        {
            if (handle == null) throw new IOException("File is closed");
            return file_detail::feof(handle!) != 0;
        }
    }

    /// Clears EOF and error indicators without changing position.
    /// Does not repair the underlying IO problem or retry data. Throws when closed.
    public void ClearError()
    {
        if (handle == null) throw new IOException("File is closed");
        file_detail::clearerr(handle!);
    }

    /// Reads up to count bytes into borrowed caller storage and returns the number read.
    /// Adds no zero terminator. Short EOF reads are normal and IO errors throw IOException.
    /// An error may leave a consumed prefix in the buffer. A closed file rejects even zero count.
    public nuint Read(char* data, nuint count)
    {
        if (handle == null) throw new IOException("File is closed");
        if (count == 0) return 0;
        nuint read = file_detail::fread(data, 1, count, handle!);
        if (read < count && file_detail::ferror(handle!) != 0)
            throw new IOException("File read failed");
        return read;
    }

    /// Returns a byte from 0 through 255, or -1 at EOF. IO errors throw IOException.
    public int ReadByte()
    {
        if (handle == null) throw new IOException("File is closed");
        int value = file_detail::fgetc(handle!);
        if (value == -1 && file_detail::ferror(handle!) != 0)
            throw new IOException("File read failed");
        return value;
    }

    /// Returns the next byte without consuming it on success, or -1 at EOF.
    /// Read or pushback failure throws IOException. Looking ahead at EOF observes EOF.
    public int PeekByte()
    {
        int value = this.ReadByte();
        if (value == -1) return -1;
        if (file_detail::ungetc(value, handle!) == -1) throw new IOException("File lookahead failed");
        return value;
    }

    /// Requires count bytes or throws EndOfStreamException on short EOF.
    /// The available prefix is already consumed and stored. Actual IO errors throw IOException.
    public void ReadExactly(char* data, nuint count)
    {
        if (this.Read(data, count) != count)
            throw new EndOfStreamException();
    }

    /// Sequentially reads and discards count bytes using a 4096-byte stack buffer.
    /// Short EOF throws EndOfStreamException. IO errors propagate after any consumed prefix.
    /// Leaves the file open without rollback. A closed file rejects even zero count.
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

    /// Borrows both files and the caller buffer, copying from the current source position.
    /// Rejects empty buffers, the same File instance or closed files before reading input.
    /// Failures may consume input or leave partial output. Both files remain open.
    public nuint CopyTo(File* destination, char* buffer, nuint capacity)
    {
        if (handle == null || !destination.IsOpen) throw new IOException("File is closed");
        return this.CopyToWriter(destination, buffer, capacity);
    }

    /// Streams bytes to a borrowed writer using caller storage without an owning intermediate.
    /// Sink exceptions propagate after the current chunk is consumed. Earlier output remains.
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

    /// Copies using a 4096-byte stack buffer, borrowing both files and leaving them open.
    public nuint CopyTo(File* destination)
    {
        char[4096] buffer;
        return this.CopyTo(destination, buffer, 4096);
    }

    /// Copies to a borrowed writer using a 4096-byte stack buffer. The source remains open.
    public nuint CopyTo(ITextWriter* destination)
    {
        char[4096] buffer;
        return this.CopyToWriter(destination, buffer, 4096);
    }

    /// Writes the counted bytes on success, preserving embedded zeros.
    /// On IO error some bytes may already be written. No automatic retry or rollback occurs.
    /// Does not flush automatically. A closed file rejects even zero count.
    public void Write(readonly(char)* data, nuint count)
    {
        if (handle == null) throw new IOException("File is closed");
        if (count == 0) return;
        // Buffered libc implementations may return count despite a flush error.
        if (file_detail::fwrite(data, 1, count, handle!) != count || file_detail::ferror(handle!) != 0)
            throw new IOException("File write failed");
    }

    /// Writes borrowed zero-terminated bytes up to the first zero, without writing the terminator.
    public void Write(immutable(char*) text)
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        this.Write(text, count);
    }

    public void WriteByte(char value) { this.Write(&value, 1); }

    /// Flushes buffered output and reports IOException on failure.
    /// Does not close or rewind the stream and does not promise storage durability.
    public void Flush()
    {
        if (handle == null) throw new IOException("File is closed");
        if (file_detail::fflush(handle!) != 0) throw new IOException("File flush failed");
    }

    /// Writes one LF byte on every platform, without an automatic flush.
    public void WriteLine() { this.Write("\n", 1); }
    public void WriteLine(immutable(char*) text) { this.Write(text); this.WriteLine(); }
    public void WriteLine(immutable(char*) data, nuint count) { this.Write(data, count); this.WriteLine(); }

    /// Closes the owned stream, reporting IOException when libc close fails.
    /// Leaves this File closed even on failure. Repeated Close does nothing.
    /// Explicit close reports errors that destruction does not report.
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
    private extern int remove(immutable(char*) path);
    private extern int rename(immutable(char*) source, immutable(char*) destination);
}
