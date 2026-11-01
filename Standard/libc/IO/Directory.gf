namespace Standard::IO;

public static class Directory
{
    // Follows links. Missing paths, other kinds, and metadata failures return false.
    public static bool Exists(readonly(char)* path) nothrow
    {
        if (path[0] == '\0') return false;
        return directory_path_detail::__gflat_stdio_path_kind(path) == 2;
    }

    // Creates one directory whose parent exists. An existing directory succeeds.
    public static void CreateDirectory(readonly(char)* path)
    {
        if (path[0] == '\0') throw new IOException("Directory path is empty");
        if (Directory::Exists(path)) return;
        if (directory_path_detail::__gflat_stdio_make_directory(path) != 0 && !Directory::Exists(path))
            throw new IOException("Directory creation failed");
    }

    // Removes one empty directory. POSIX rejects a final symbolic link.
    // Windows may remove the link itself, leaving its target intact.
    public static void Remove(readonly(char)* path)
    {
        if (directory_path_detail::__gflat_stdio_remove_directory(path) != 0)
            throw new IOException("Directory removal failed");
    }
}

namespace directory_path_detail
{
    private extern int __gflat_stdio_path_kind(readonly(char)* path) nothrow;
    private extern int __gflat_stdio_make_directory(readonly(char)* path) nothrow;
    private extern int __gflat_stdio_remove_directory(readonly(char)* path) nothrow;
}
