using std::Text;
using std;

extern int printf(readonly(char)* text, ...);

struct Player : IFormattable
{
    public int score;
    public Player(int value) { score = value; }

    public immutable void Format(ITextWriter* output)
    {
        output.Write("Player(score=", 13);
        std::Format(score, output);
        output.Write(")", 1);
    }
}

int main()
{
    Player player = new Player(42);
    String message = s$"Hello {s"Ada"}: {player}; accuracy: {0.875f}";
    message.Append("!", 1);
    printf("%s\n", message.Data);
    return 0;
}
