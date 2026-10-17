struct HeapSample { public int Value; }

int Run()
{
    HeapSample* sample = new* HeapSample();
    sample.Value = 42;
    int answer = sample.Value;
    delete sample;
    return answer - 42;
}

void Start() { Platform::Exit((uint)Run()); }
