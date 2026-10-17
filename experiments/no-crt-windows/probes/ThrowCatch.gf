int Run()
{
    try { throw new Exception("exception probe", 42); }
    catch (ref Exception error) { return error.GetCode() - 42; }
}

void Start() { Platform::Exit((uint)Run()); }
