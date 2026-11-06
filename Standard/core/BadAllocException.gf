namespace Standard
{
    public class BadAllocException : Exception
    {
        public BadAllocException(readonly(char)* message = "Allocation failed") : base(message, 1) { }
    }
}

namespace __gflat_runtime
{
    public alias AllocationFailure = global::Standard::BadAllocException;
}
