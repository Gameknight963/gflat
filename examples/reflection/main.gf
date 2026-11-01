using Standard::Text;

struct Reading
{
    public int Count;
    public String Name = new();
    private int secret;
}

int main()
{
    Reading reading = new();
    reading.Count = 42;
    reading.Name = s"Ada";

    char[128] storage;
    BufferWriter output = new(storage, 128);
    WriteFields(ref reading, &output);
    Console::Write(output.Data, output.Length);
    return 0;
}
