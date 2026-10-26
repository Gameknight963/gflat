int Run()
{
    std::Text::String text = std::Text::String::From("core without libc");
    if (text.Length != (nuint)17) return 1;
    if (!Platform::Write(text.Data, (uint)text.Length)) return 2;
    return 0;
}

void Start() { Platform::Exit((uint)Run()); }
