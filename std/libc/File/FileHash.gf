namespace std;

// Optional file IO convenience. SHA256 itself remains independent of libc.
public static class FileHash
{
    // Hash remaining bytes without closing or rewinding the borrowed file.
    public static byte[32] SHA256(File* input, char* buffer, nuint capacity)
    {
        if (!input.IsOpen) throw new IOException("File is closed");
        if (capacity == 0) throw new IOException("File hash buffer must not be empty");
        std::SHA256 state = new();
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
}
