struct Sample
{
    public int Value;
}

int Run()
{
    // Stack construction and copying a fixed buffer require no library allocator.
    int[4] values = [10, 20, 30, 40];
    int[4] copy = values;
    copy[0] = 99;
    if (values[0] != 10 || copy[0] != 99 || copy[3] != 40) return 1;

    // Exercise stack growth across several Windows guard pages.
    char[32768] pages;
    for (int i = 0; i < 32768; i++) pages[i] = (char)(i % 127);
    for (int i = 0; i < 32768; i++)
        if (pages[i] != (char)(i % 127)) return 5;

    void*? storage = Memory::Allocate((nuint)sizeof(Sample));
    if (storage == null) return 2;
    Sample* sample = new* Sample() at (Sample*)storage!;
    sample.Value = 42;
    int answer = sample.Value;
    sample.~Sample();
    Memory::Free(storage!);
    if (answer != 42) return 3;

    if (!Platform::Write("gflat without CRT\r\n", 19)) return 4;
    return 0;
}

// The linker selects this function directly. There is no CRT entry point or main.
void Start() { Platform::Exit((uint)Run()); }
