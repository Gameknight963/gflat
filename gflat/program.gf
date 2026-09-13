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
// Main Entry Point
// ========================================================

int main()
{
    printf("=== gflat Showcase: const & readonly Systems ===\n\n");

    // --- 1. Const Evaluation & Array Sizing ---
    printf("-- 1. Compile-Time Evaluation & Constants --\n");
    printf("Factorial(4) = %d (computed at compile time)\n", ItemCount);
    printf("BufferCapacity = %d\n", BufferCapacity);
    printf("Origin Point = (%d, %d)\n", Origin.x, Origin.y);

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
    printf("Lambda (20 + 22) = %d\n", addFn(20, 22));

    return 0;
}