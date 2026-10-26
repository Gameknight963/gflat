using std::Text;
namespace std;

// Optional writer adapter; the caller-buffer formatter itself stays freestanding.
void Format(this extralong value, ITextWriter* output)
{
    char[40] buffer;
    nuint written = 0;
    IntegerFormatting::TryFormat(value, buffer, 40, ref written);
    output.Write(buffer, written);
}
