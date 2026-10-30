using std::Text;
namespace std;

// Optional writer adapter; the caller-buffer formatter itself stays freestanding.
void Format(extralong value, ITextWriter* output)
=> value.Format(output);

extension extralong
{
    public void Format(ITextWriter* output)
    {
        char[40] buffer;
        nuint written = 0;
        IntegerFormatting::TryFormat((*this), buffer, 40, ref written);
        output.Write(buffer, written);
    }
}
