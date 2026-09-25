using std;

extern int printf(readonly(char)* text, ...);

struct Player : IStringConvertible
{
    public int score;
    public Player(int value) { score = value; }

    public readonly char* ToString() throws
    {
        String text = s$"Player(score={score})";
        return text.ToString();
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
