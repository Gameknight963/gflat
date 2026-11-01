namespace Standard::IO;

public static class Directory
{
    // Follows links. Missing paths, other kinds, and metadata failures return false.
    public static bool Exists(readonly(char)* path) nothrow
    {
        if (path[0] == '\0') return false;
        return directory_path_detail::__gflat_stdio_path_kind(path) == 2;
    }
}

namespace directory_path_detail
{
    private extern int __gflat_stdio_path_kind(readonly(char)* path) nothrow;
}
