#include "gcr.h"
#include "colors.h"
#include <iostream>
#include <filesystem>
#include <fstream>
#include <vector>
#include <chrono>

#ifdef _WIN32
#include <windows.h>

static void enableAnsi()
{
    HANDLE handle = GetStdHandle(STD_OUTPUT_HANDLE);
    DWORD mode = 0;
    GetConsoleMode(handle, &mode);
    SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
}
#else
static void enableAnsi() {}
#endif

int main(int argc, char* argv[])
{
    enableAnsi();
    std::chrono::steady_clock::time_point start = std::chrono::steady_clock::now();

    if (argc < 2)
    {
        std::cerr << colors::BrightRed << "no file specified" << colors::Reset;
        return 257;
    }
    if (!std::filesystem::exists(argv[1]))
    {
        std::cerr << colors::BrightRed << argv[1] << ": no such file" << colors::Reset;
        return 258;
    }

    std::ifstream file(argv[1], std::ios::binary);
    if (!file)
    {
        std::cerr << colors::BrightRed << argv[1] << ": could not open file" << colors::Reset;
        return 259;
    }

    std::vector<uint8_t> bytecode{
        std::istreambuf_iterator<char>(file),
        std::istreambuf_iterator<char>()
    };

    gvm_registerHost("io.print_int", []() {
        std::cout << vm_pop();
        });
    gvm_registerHost("io.print_char", []() {
        std::cout << static_cast<char>(vm_pop());
        });
    gvm_registerHost("io.print_newline", []() {
        std::cout << '\n';
        });

    int code = vm_run(bytecode);

    if (code == 0)
        std::cout << colors::BrightBlack << "\nprogram exited with code " << code;
    else
        std::cerr << colors::BrightRed << "\nunsuccessful exit: " << code << colors::BrightBlack;

    std::chrono::steady_clock::time_point end = std::chrono::steady_clock::now();
    std::cout << "\ntime elapsed: "
        << std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count()
        << "ms" << colors::Reset;

    return code;
}