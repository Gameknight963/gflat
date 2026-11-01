using Standard;
using Standard::Text;

void WriteFields<T>(ref immutable T value, ITextWriter* output)
{
    static foreach (reflection field in reflect(T).Fields)
    {
        static if (field.IsPublic)
        {
            output.Write(field.Name, CString::Length(field.Name));
            output.Write(" = ", 3);
            value.[:field:].Format(output);
            output.Write("\n", 1);
        }
    }
}
