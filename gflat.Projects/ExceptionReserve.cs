using System.Globalization;

namespace gflat.Projects;

/// <summary>Link-time storage used only by the native exception runtime.</summary>
public static class ExceptionReserve
{
    public const int DefaultBytes = 65536;

    public static int Parse(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int bytes))
            throw new ArgumentException("Exception reserve bytes must be an integer between 0 and 2147483647; 0 disables the reserve.");
        return bytes;
    }
}
