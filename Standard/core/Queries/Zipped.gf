namespace Standard::Queries;

public struct Zipped<TFirst, TSecond>
{
    public TFirst First;
    public TSecond Second;

    public Zipped(TFirst first, TSecond second)
    {
        First = move first;
        Second = move second;
    }
}
