using Standard::Text;
namespace Standard;

// Owning copies, separate from the C-string-only Environment API.
public static class EnvironmentText
{
    public static String GetVariable(readonly(char)* name)
    {
        readonly(char)*? value = Environment::GetVariable(name);
        if (value == null) throw new Exception("Environment variable not found");
        return String::From(value!);
    }

    // Missing clears output and returns false. Allocation failure leaves output
    // unchanged because the new owning copy is constructed before replacement.
    public static bool TryGetVariable(readonly(char)* name, ref String output)
    {
        readonly(char)*? value = Environment::GetVariable(name);
        if (value == null) { output.Clear(); return false; }
        String copy = String::From(value!);
        output = move copy;
        return true;
    }
}
