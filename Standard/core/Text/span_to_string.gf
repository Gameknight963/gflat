using Standard::Memory;

namespace Standard::Text;

extension Span<readonly(char)>
{
    // Copies counted bytes, including zeros. Empty views do not allocate.
    public immutable String ToString()
    {
        Span<readonly(char)> view = *this;
        if (view.IsEmpty) return new String();
        return String::From(view.Data!, view.Length);
    }
}

extension Span<char>
{
    // Copies counted bytes, including zeros. Empty views do not allocate.
    public immutable String ToString()
    => (*this).AsImmutable().ToString();
}
