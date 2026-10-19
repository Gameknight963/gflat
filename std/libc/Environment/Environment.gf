namespace std;

// Borrows libc's environment storage. Never free or modify the returned bytes.
public static class Environment
{
    // Null means missing; a nonnull pointer to '\0' is an existing empty value.
    public static readonly(char)*? GetVariable(readonly(char)* name) nothrow
        => environment_detail::getenv(name);
}

namespace environment_detail
{
    private extern readonly(char)*? getenv(readonly(char)* name) nothrow;
}
