#include "features.gfh"

extern "C" int printf(readonly(char)* format, ...);

int main()
{
    int JOIN(answer_, VERSION) = 42;
    printf("%s = %s, answer = %d\n", STRINGIFY(@VERSION), STRINGIFY(VERSION), answer_12);
    return 0;
}
