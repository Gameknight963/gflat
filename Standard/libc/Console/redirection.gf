namespace Console;

// Query the current CRT streams. Files, pipes, and nonterminal devices are redirected.
public bool IsInputRedirected { get nothrow => redirect_detail::__gflat_stdio_is_redirected(0) != 0; }
public bool IsOutputRedirected { get nothrow => redirect_detail::__gflat_stdio_is_redirected(1) != 0; }
public bool IsErrorRedirected { get nothrow => redirect_detail::__gflat_stdio_is_redirected(2) != 0; }

namespace redirect_detail
{
    private extern int __gflat_stdio_is_redirected(int stream) nothrow;
}
