using System.Text;
using gflat.CompileExceptions;

namespace gflat;

internal static class StringLiteralEncoding
{
    public static byte[] Bytes(string raw, int line = 0)
    {
        var text = new StringBuilder();
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c != '\\') { text.Append(c); continue; }
            if (++i == raw.Length) throw new TypeCheckException("Incomplete string escape", line);
            text.Append(raw[i] switch
            {
                'n' => '\n', 'r' => '\r', 't' => '\t', '0' => '\0',
                '\\' => '\\', '"' => '"', '\'' => '\'',
                _ => throw new TypeCheckException($"Unsupported string escape '\\{raw[i]}'", line)
            });
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    public static string LlvmBytes(byte[] bytes) => string.Concat(bytes.Select(b => "\\" + b.ToString("X2")));
}
