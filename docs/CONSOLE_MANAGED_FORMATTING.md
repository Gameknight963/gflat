# Print managed objects

Managed formattable objects can be passed directly to Console:

```csharp
class Message : IFormattable
{
    public immutable void Format(ITextWriter* output)
    {
        output.Write("hello", 5);
    }
}

int main()
{
    Message^ message = new^ Message();
    Console::WriteLine(message);
    Console::WriteLine(new^ Message());
    return 0;
}
```

The call copies the managed reference and keeps the object rooted during
formatting. References retain their collector type. Value owners continue to
use the explicit ref overload.
