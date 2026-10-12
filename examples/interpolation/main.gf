using std;

extern int printf(readonly(char)* text, ...);

struct Player : IFormattable
{
    public int score;
    public Player(int value) { score = value; }

    public readonly void Format(ITextWriter* output) throws
    {
        output.Write(c"Player(score=", 13);
        std::Format(score, output);
        output.Write(c")", 1);
    }
}

int main()
{
    Player player = new Player(42);
    String message = s$"Hello {s"Ada"}: {player}; accuracy: {0.875f}";
    message.Append(c"!", 1);
    printf(c"%s\n", message.Data);
    return 0;
}
