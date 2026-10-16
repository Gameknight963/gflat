using System.Globalization;

namespace gflat;

internal static class FloatingLiteralEncoding
{
    public static double Value(Token token)
    {
        string text = token.Text.TrimEnd('f', 'F', 'd', 'D');
        // Parse at the literal's declared precision. Parsing a float through
        // double first can lose the digits that decide a halfway case.
        return token.Kind == TokenKind.FloatLiteral
            ? float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)
            : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
