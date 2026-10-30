using Standard::Hashing;
namespace Standard::IO;

// Optional file IO convenience. Core SHA256 and SHA512 remain independent of libc.
public static class FileHash
{
    // Hash remaining bytes without closing or rewinding the borrowed file.
    public static byte[32] SHA256(File* input, char* buffer, nuint capacity)
    {
        if (!input.IsOpen) throw new IOException("File is closed");
        if (capacity == 0) throw new IOException("File hash buffer must not be empty");
        Standard::Hashing::SHA256 state = new();
        while (true)
        {
            nuint count = input.Read(buffer, capacity);
            if (count == 0) break;
            if (!state.TryAppend(buffer, count))
                throw new Exception("SHA-256 input exceeds the message length limit");
        }
        return state.GetHash();
    }

    public static byte[32] SHA256(File* input)
    {
        char[4096] buffer;
        return FileHash::SHA256(input, buffer, 4096);
    }

    // Own the file for this call and observe close failure before returning.
    public static byte[32] SHA256(immutable(char*) path)
    {
        File input = File::OpenRead(path);
        byte[32] digest = FileHash::SHA256(&input);
        input.Close();
        return digest;
    }

    /// Hashes remaining bytes into a 64-byte digest without closing or rewinding the file.
    /// Rejects closed files and zero capacity before reading; IO failures may consume input.
    public static byte[64] SHA512(File* input, char* buffer, nuint capacity)
    {
        if (!input.IsOpen) throw new IOException("File is closed");
        if (capacity == 0) throw new IOException("File hash buffer must not be empty");
        Standard::Hashing::SHA512 state = new();
        while (true)
        {
            nuint count = input.Read(buffer, capacity);
            if (count == 0) break;
            if (!state.TryAppend(buffer, count))
                throw new Exception("SHA-512 input exceeds the message length limit");
        }
        return state.GetHash();
    }

    /// Borrows an open file, using a 4096-byte stack buffer and leaving it at EOF.
    public static byte[64] SHA512(File* input)
    {
        char[4096] buffer;
        return FileHash::SHA512(input, buffer, 4096);
    }

    /// Opens a binary file and closes it before returning an independent 64-byte digest.
    public static byte[64] SHA512(immutable(char*) path)
    {
        File input = File::OpenRead(path);
        byte[64] digest = FileHash::SHA512(&input);
        input.Close();
        return digest;
    }
}
