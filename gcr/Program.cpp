#include "opcodes.h"
#include <iostream>
#include <filesystem>
#include <string>
#include <fstream>
#include <cstdint>
#include <vector>
#include <iterator>
#include <stack>
#include "colors.h"

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
static void enableAnsi() {} // noop on linux
#endif


int main(int argc, char *argv[])
{
	enableAnsi();

	if (argc < 2)
	{
		std::cout << "no file specified";
		return 257;
	}
	if (!std::filesystem::exists(argv[1]))
	{
		std::cout << argv[1] << ": no such file";
		return 258;
	}
	std::string path = argv[1];
	std::ifstream file(path, std::ios::binary);
	if (!file)
	{
		std::cerr << path << ": could not open file";
		return 259;
	}

	std::vector<uint8_t> bytecode{
		std::istreambuf_iterator<char>(file),
		std::istreambuf_iterator<char>()
	};
	
	size_t ip = 0;
	std::stack<int32_t> stack;
	
	while (ip < bytecode.size())
	{
		OpCode instruction = static_cast<OpCode>(bytecode[ip++]);

		switch (instruction)
		{
			case OpCode::HALT:
			{
				uint8_t code = bytecode[ip++];
				if (code == 0)
					std::cout << colors::BrightBlack << "\nprogram exited with code " << static_cast<int>(code) << colors::Reset;
				else
					std::cerr << "\nunsuccessful exit: " << static_cast<int>(code);
				return code;
			}
			case OpCode::PUSH:
			{
				int32_t value = bytecode[ip] | (bytecode[ip + 1] << 8) | (bytecode[ip + 2] << 16) | (bytecode[ip + 3] << 24);
				ip += 4;
				stack.push(value);
				break;
			}
			case OpCode::POP:
			{
				stack.pop();
				break;
			}
			case OpCode::ADD:
			{
				int32_t b = stack.top();
				stack.pop();
				int32_t a = stack.top();
				stack.pop();
				stack.push(a + b);
				break;
			}
			case OpCode::PRINT:
			{
				int32_t a = stack.top();
				stack.pop();
				std::cout << static_cast<char>(a);
				break;
			}
			default:
			{
				std::cerr << "\nunknown instruction: 0x" << std::hex << static_cast<int>(bytecode[ip-1]);
				return 256;
			}
		}
	}

	std::cerr << "\nno HALT instruction at the end of the program";
	return 1;
}