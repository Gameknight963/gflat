struct ConstructedSample
{
    public int Value;
    public ConstructedSample(int value) { Value = value; }
}

int Run()
{
    void*? storage = Memory::Allocate((nuint)sizeof(ConstructedSample));
    if (storage == null) return 2;
    ConstructedSample* sample = new* ConstructedSample(42) at (ConstructedSample*)storage!;
    int answer = sample.Value;
    sample.~ConstructedSample();
    Memory::Free(storage!);
    return answer - 42;
}

void Start() { Platform::Exit((uint)Run()); }
