using System;
using System.IO;

namespace gflat.Projects;

public static class RuntimeLibrary
{
    public static string Parse(string value)
    {
        foreach (string name in new[] { "MultiThreaded", "MultiThreadedDLL", "MultiThreadedDebug", "MultiThreadedDebugDLL" })
            if (name.Equals(value, StringComparison.OrdinalIgnoreCase)) return name;
        throw new ArgumentException("RuntimeLibrary must be MultiThreaded, MultiThreadedDLL, MultiThreadedDebug, or MultiThreadedDebugDLL.");
    }

    public static string[] CompilerArguments(string value)
    {
        value = Parse(value);
        if (Path.DirectorySeparatorChar != '\\')
        {
            if (value != "MultiThreaded") throw new ArgumentException("RuntimeLibrary selects a Windows C runtime.");
            return Array.Empty<string>();
        }
        return new[] { "-fms-runtime-lib=" + (value == "MultiThreadedDLL" ? "dll" : value == "MultiThreadedDebugDLL" ? "dll_dbg" :
            value == "MultiThreadedDebug" ? "static_dbg" : "static") };
    }
}
