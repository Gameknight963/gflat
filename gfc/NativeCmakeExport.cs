using System.Text;
using System.Text.RegularExpressions;
using gflat.Projects;

namespace gflat;

internal static class NativeCmakeExport
{
    internal static string Generate(string library, bool shared)
    {
        var reference = new BinaryProjectReference(library, shared);
        string directory = Path.GetDirectoryName(reference.LibraryPath)!;
        string name = Regex.Replace(Path.GetFileNameWithoutExtension(library), "[^A-Za-z0-9_.+-]", "_");
        if (name.Length == 0) name = "library";
        string target = "Gflat::" + name;
        var text = new StringBuilder("# Generated native library import. Keep this file beside the binary and header.\n");
        text.Append("if(TARGET ").Append(target).Append(")\n  return()\nendif()\n");
        text.Append("add_library(").Append(target).Append(shared ? " SHARED" : " STATIC").AppendLine(" IMPORTED GLOBAL)");
        Property("IMPORTED_LOCATION", PathValue(reference.LibraryPath));
        if (shared && OperatingSystem.IsWindows()) Property("IMPORTED_IMPLIB", PathValue(reference.LinkPath));
        Property("INTERFACE_INCLUDE_DIRECTORIES", "\"${CMAKE_CURRENT_LIST_DIR}\"");
        Property("INTERFACE_COMPILE_FEATURES", Quote("cxx_std_11"));
        foreach (var dependency in reference.NativeInputs.Libraries.Skip(1))
            Property("INTERFACE_LINK_LIBRARIES", dependency.Kind == "File" ? PathValue(dependency.Value) : Quote(dependency.Value), append: true);
        foreach (string path in reference.NativeInputs.Directories)
            Property("INTERFACE_LINK_DIRECTORIES", PathValue(path), append: true);
        if (reference.RuntimeLibrary is { } runtime)
        {
            Property("GFLAT_RUNTIME_LIBRARY", Quote(runtime));
            Property("INTERFACE_GFLAT_RUNTIME_LIBRARY", Quote(runtime));
            Property("COMPATIBLE_INTERFACE_STRING", Quote("GFLAT_RUNTIME_LIBRARY"), append: true);
        }
        foreach (string path in reference.RuntimeFiles)
            Property("GFLAT_RUNTIME_FILES", PathValue(Path.Combine(directory, Path.GetFileName(path))), append: true);
        text.AppendLine("""

            if(NOT COMMAND gflat_deploy_runtime)
              function(gflat_deploy_runtime consumer library)
                get_target_property(_files "${library}" GFLAT_RUNTIME_FILES)
                if(_files)
                  add_custom_command(TARGET "${consumer}" POST_BUILD
                    COMMAND "${CMAKE_COMMAND}" -E copy_if_different ${_files} "$<TARGET_FILE_DIR:${consumer}>"
                    COMMAND_EXPAND_LISTS VERBATIM)
                endif()
              endfunction()
            endif()
            """);
        return text.ToString();

        string PathValue(string path) => "\"${CMAKE_CURRENT_LIST_DIR}/" + Escape(Path.GetRelativePath(directory, path)) + "\"";
        void Property(string property, string value, bool append = false)
            => text.Append("set_property(TARGET ").Append(target).Append(append ? " APPEND" : "")
                .Append(" PROPERTY ").Append(property).Append(' ').Append(value).AppendLine(")");
    }

    private static string Quote(string value) => "\"" + Escape(value) + "\"";
    private static string Escape(string value) => value.Replace('\\', '/').Replace("$", "\\$", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal);
}
