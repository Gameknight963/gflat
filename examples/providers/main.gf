alias Sum = Hashing::Checksums<Algorithms::Sum>;
alias Xor = Hashing::Checksums<Algorithms::Xor>;

// A consumer can use qualified names without importing all the static members.
bool IsExpected(Sum::Result result)
    => result.Value == (uint)294 && result.Length == (nuint)3;

int main()
{
    Sum::Result sum = Sum::Compute("abc", 3);
    Xor::Result xor = Xor::Compute("abc", 3);
    if (!IsExpected(sum)) return 1;
    if (xor.Value != (uint)96 || xor.Length != (nuint)3) return 2;
    return 0;
}
