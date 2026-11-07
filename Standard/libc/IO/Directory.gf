using Standard::Text;

namespace Standard::IO;

public static class Directory
{
    public static String GetCurrentDirectory()
    {
        String result = new();
        nuint size = 64;
        while (true)
        {
            result.Resize(size);
            int status = directory_path_detail::__gflat_stdio_get_current_directory((char*)result.Data, size + 1);
            if (status == 1)
            {
                nuint length = 0;
                while (length < size && result[length] != '\0') length++;
                result.Resize(length);
                return move result;
            }
            if (status != -1) throw new IOException("Current directory query failed");
            nuint maximum = (nuint)0 - (nuint)2;
            if (size > maximum / 2) throw new IOException("Current directory path is too long");
            size *= 2;
        }
    }

    public static void SetCurrentDirectory(readonly(char)* path)
    {
        if (path[0] == '\0') throw new IOException("Directory path is empty");
        if (directory_path_detail::__gflat_stdio_set_current_directory(path) != 0)
            throw new IOException("Current directory change failed");
    }

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
    private extern "C" int __gflat_stdio_get_current_directory(char* buffer, nuint capacity) nothrow;
    private extern "C" int __gflat_stdio_set_current_directory(readonly(char)* path) nothrow;
    private extern "C" int __gflat_stdio_path_kind(readonly(char)* path) nothrow;
    private extern "C" int __gflat_stdio_make_directory(readonly(char)* path) nothrow;
    private extern "C" int __gflat_stdio_remove_directory(readonly(char)* path) nothrow;
}
