namespace gflat;

public readonly record struct SourceSpan(int Start, int Length, int Line, int Column, SourceFile? Source = null)
{
    public SourceId? SourceId => Source?.Id;
}
