extern int printf(char* fmt, ...);

// ========================================================
// 1. Const Functions & Compile-Time Evaluation (Stage 2)
// ========================================================

const int Factorial(int n)
{
    if (n <= 1)
    {
        return 1;
    }
    return n * Factorial(n - 1);
}

const int Square(int x)
{
    return x * x;
}

// Compile-time constants computed from const function calls & math
const int ItemCount = Factorial(4);        // 24 at compile time!
const int HeaderSize = 8;
const int BufferCapacity = ItemCount + HeaderSize; // 32 at compile time!

// ========================================================
// 2. Structs, Readonly Methods & Operators (Stage 1)
// ========================================================

struct Point
{
    int x;
    int y;

    Point()
    {
        this.x = 0;
        this.y = 0;
    }

    Point(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    // Readonly method: cannot mutate 'this' fields
    readonly void Print()
    {
        printf("Point(%d, %d)\n", this.x, this.y);
    }

    readonly int LengthSquared()
    {
        return this.x * this.x + this.y * this.y;
    }

    void Move(int dx, int dy)
    {
        this.x += dx;
        this.y += dy;
    }

    public static Point operator +(Point a, Point b)
    {
        Point res;
        res.x = a.x + b.x;
        res.y = a.y + b.y;
        return res;
    }

    public static bool operator ==(Point a, Point b)
    {
        return a.x == b.x && a.y == b.y;
    }
}

// Comptime constant value-struct
const Point Origin = new Point(0, 0);

// Function accepting a readonly pointer: cannot mutate pointee!
void InspectPoint(readonly Point* p)
{
    printf("Inspecting Point: ");
    p.Print(); // Allowed because Print() is a readonly method
    printf("  LengthSquared: %d\n", p.LengthSquared());
}

// ========================================================
// 3. Const Parameters & Array Sizing
// ========================================================

// Const parameter: caller MUST pass a compile-time constant
void InitializeBuffer(const int size, int* buf)
{
    for (int i = 0; i < size; i = i + 1)
    {
        buf[i] = (i + 1) * 10;
    }
}

// ========================================================
// 4. Readonly Fields & OOP Inheritance (Stage 1)
// ========================================================

abstract class Entity
{
    // Readonly field: can only be assigned in constructor
    public readonly int id;
    public int health;

    public Entity(int id, int health)
    {
        this.id = id;
        this.health = health;
    }

    public abstract void Describe();

    public readonly void PrintStatus()
    {
        printf("[Entity #%d] Health: %d\n", this.id, this.health);
    }

    public void TakeDamage(int amount)
    {
        this.health -= amount;
        if (this.health < 0)
        {
            this.health = 0;
        }
    }
}

class Player : Entity
{
    public int score;

    public Player(int id, int health, int score) : base(id, health)
    {
        this.score = score;
    }

    public override void Describe()
    {
        printf("Player #%d - Health: %d, Score: %d\n", this.id, this.health, this.score);
    }
}

class Monster : Entity
{
    public int damage;

    public Monster(int id, int health, int damage) : base(id, health)
    {
        this.damage = damage;
    }

    public override void Describe()
    {
        printf("Monster #%d - Health: %d, Damage: %d\n", this.id, this.health, this.damage);
    }
}

// ========================================================
// 5. Interfaces & Polymorphism
// ========================================================

interface IShape
{
    int Area();
    int Perimeter();
}

struct Rect : IShape
{
    int w;
    int h;

    Rect(int w, int h)
    {
        this.w = w;
        this.h = h;
    }

    int Area()
    {
        return this.w * this.h;
    }

    int Perimeter()
    {
        return 2 * (this.w + this.h);
    }
}

void PrintShape(char* label, IShape* shape)
{
    printf("%s -> Area: %d, Perimeter: %d\n", label, shape.Area(), shape.Perimeter());
}

// ========================================================
// 6. Custom String Prefixes & Built-in C-Strings
// ========================================================

[string_prefix("sql")]
struct SqlQuery
{
    readonly char* query;

    const SqlQuery(readonly char* raw)
    {
        this.query = raw;
    }
}

[string_prefix("hash")]
const uint HashString(readonly char* str)
{
    uint h = 2166136261u; // FNV-1a 32-bit offset basis
    for (int i = 0; str[i] != '\0'; i++)
    {
        h = (h ^ (uint)str[i]) * 16777619u;
    }
    return h;
}

// Compile-time string prefix evaluation!
const uint UserTableSlots = hash"users" % 8u + 4u;

// ========================================================
// 7. Compile-Time Generics / Templates (Stage 3)
// ========================================================

struct Pair<T, U>
{
    T first;
    U second;

    Pair(T first, U second)
    {
        this.first = first;
        this.second = second;
    }

    readonly void Print()
    {
        printf("Pair(%d, %d)\n", this.first, this.second);
    }
}

T Max<T>(T a, T b)
{
    if (a > b)
    {
        return a;
    }
    return b;
}

// Interface-constrained generic function
int ComputeArea<T : IShape>(T* shape)
{
    return shape.Area();
}

// Base-class constrained generic function
int GetEntityHealth<T : Entity>(T* e)
{
    return e.health;
}

// ========================================================
// Main Entry Point
// ========================================================

int main()
{
    printf("=== gflat Showcase: const & readonly Systems ===\n\n");

    // --- 1. Const Evaluation & Array Sizing ---
    printf("-- 1. Compile-Time Evaluation, Constants & Type Info --\n");
    printf("Factorial(4) = %d (computed at compile time)\n", ItemCount);
    printf("BufferCapacity = %d\n", BufferCapacity);
    printf("Origin Point = (%d, %d)\n", Origin.x, Origin.y);
    printf("sizeof(int) = %d, sizeof(Point) = %d, sizeof(void*) = %d\n",
           sizeof(int), sizeof(Point), sizeof(void*));
    printf("nameof(Point) = '%s', nameof(Origin) = '%s', nameof(int) = '%s'\n",
           nameof(Point), nameof(Origin), nameof(int));

    // Array sized by compile-time constant expression!
    int[BufferCapacity] myBuffer;
    InitializeBuffer(BufferCapacity, myBuffer);
    printf("myBuffer[0] = %d, myBuffer[5] = %d, myBuffer[31] = %d\n\n",
           myBuffer[0], myBuffer[5], myBuffer[31]);

    // --- 2. Readonly Methods & Readonly Pointers ---
    printf("-- 2. Readonly Methods & Pointers --\n");
    Point pt = new Point(3, 4);
    pt.Print();

    // Pass mutable pointer to readonly Point* parameter
    InspectPoint(&pt);

    pt.Move(2, -1);
    printf("After Move(2, -1): ");
    pt.Print();

    Point pt2 = new Point(5, 10);
    Point ptSum = pt + pt2;
    printf("Operator + result: ");
    ptSum.Print();
    printf("\n");

    // --- 3. Readonly Fields & Inheritance ---
    printf("-- 3. Readonly Fields & OOP --\n");
    Player player = new Player(101, 100, 500);
    player.Describe();
    player.TakeDamage(25);
    player.PrintStatus();

    Monster monster = new Monster(999, 50, 15);
    Entity* e1 = &player;
    Entity* e2 = &monster;
    e1.Describe();
    e2.Describe();
    printf("\n");

    // --- 4. Heap Allocation, Defer & Interfaces ---
    printf("-- 4. Heap Allocation, Defer & Interfaces --\n");
    defer printf("Deferred cleanup: main exited successfully!\n");

    Rect stackRect = new Rect(6, 7);
    PrintShape("Stack Rect", &stackRect);

    Rect* heapRect = new* Rect(12, 5);
    defer heapRect->free();
    PrintShape("Heap Rect", heapRect);

    // Lambda expressions
    int(int, int)* addFn = (int a, int b) => a + b;
    printf("Lambda (20 + 22) = %d\n\n", addFn(20, 22));

    // --- 5. Custom String Prefixes & Built-in C-Strings ---
    printf("-- 5. Custom String Prefixes --\n");
    readonly char* cstr = c"Built-in C-string literal: Hello from gflat!";
    printf("%s\n", cstr);

    SqlQuery query = sql"SELECT id, username, email FROM accounts WHERE status = 'active';";
    printf("SqlQuery.query = '%s'\n", query.query);

    printf("Compile-time FNV-1a hash of 'users' = 0x%08X\n", hash"users");
    printf("Compile-time sized table slots: %d slots\n", UserTableSlots);
    int[UserTableSlots] userSlots;
    printf("sizeof(userSlots) = %d bytes\n\n", sizeof(userSlots));

    // --- 6. Compile-Time Generics / Templates ---
    printf("-- 6. Compile-Time Generics / Templates --\n");
    Pair<int, int> intPair = new Pair<int, int>(100, 200);
    printf("Generic Pair<int, int>: ");
    intPair.Print();

    // Type inference on generic function call: Max(42, 17) -> Max<int>
    int maxVal = Max(42, 17);
    printf("Generic Max(42, 17) with type inference = %d\n", maxVal);

    // Explicit type argument on generic function call: Max<int>(10, 99)
    int maxExplicit = Max<int>(10, 99);
    printf("Generic Max<int>(10, 99) = %d\n", maxExplicit);

    // Interface-constrained generic function: ComputeArea<Rect>
    int shapeArea = ComputeArea(&stackRect);
    printf("Constrained ComputeArea(&stackRect) = %d\n", shapeArea);

    // Base-class constrained generic function: GetEntityHealth<Player>
    int pHealth = GetEntityHealth(&player);
    printf("Constrained GetEntityHealth(&player) = %d\n\n", pHealth);

    return 0;
}