using System.Globalization;

namespace gflat.Projects;

public static class OptimizationLevel
{
    public static int Parse(string value)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int level) && level is >= 0 and <= 3)
            return level;
        throw new ArgumentException("Optimization level must be 0, 1, 2, or 3.");
    }
}
