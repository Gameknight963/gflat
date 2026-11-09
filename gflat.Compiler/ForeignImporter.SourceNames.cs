using System.Text;

namespace gflat;

internal static partial class ForeignImporter
{
    private static string SourcePathLiteral(string path)
    {
        var literal = new StringBuilder("\"");
        foreach (char character in path)
        {
            switch (character)
            {
                case '\\': literal.Append('/'); break;
                case '"': literal.Append("\\\""); break;
                case '\n': literal.Append("\\n"); break;
                case '\r': literal.Append("\\r"); break;
                case '\t': literal.Append("\\t"); break;
                default:
                    if (character < ' ' || character == '\u007f')
                        literal.Append('\\').Append(Convert.ToString(character, 8).PadLeft(3, '0'));
                    else literal.Append(character);
                    break;
            }
        }
        return literal.Append('"').ToString();
    }
}
