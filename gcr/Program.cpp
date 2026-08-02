#include "opcodes.h"
#include <iostream>
#include <filesystem>
#include <string>
#include <fstream>
#include <cstdint>
#include <vector>
#include <iterator>
#include <iomanip>
#include <chrono>
#include <functional>
#include <unordered_map>
#include "colors.h"
#include "malloc_config.h"
#include "malloc.h"
#include <sstream>
#include <cstring>

#ifdef _WIN32
#include <windows.h>
#include <cstdlib>

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

const size_t STACK_SIZE = 4096;
const size_t CALL_STACK_SIZE = 256;

static int32_t stack[STACK_SIZE];
static int32_t sp = -1;

static size_t callStack[CALL_STACK_SIZE];
static int32_t csp = -1;

static std::unordered_map<std::string, std::function<void()>> hostFunctions;

static void vmError(size_t ip, const std::string& message)
{
	std::cerr << colors::BrightRed << "\n" << message << " at byte 0x"
		<< std::hex << std::setw(2) << std::setfill('0')
		<< (ip - 1) << colors::Reset;
	std::exit(256);
}

static int32_t pop(size_t ip)
{
	if (sp < 0)
		vmError(ip, "stack underflow");
	return stack[sp--];
}

static void push(int32_t value, size_t ip)
{
	if (sp >= (int32_t)STACK_SIZE - 1)
		vmError(ip, "stack overflow");
	stack[++sp] = value;
}

const size_t MAX_LOCALS = 256;
const size_t MAX_FRAMES = 64;

const size_t HEAP_SIZE = 1024 * 1024;
static uint8_t heap[HEAP_SIZE];
static mspace heapSpace;

struct Frame
{
	int32_t locals[MAX_LOCALS];
	int32_t localCount;
};

static Frame frames[MAX_FRAMES];
static int32_t fp = -1;

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
	std::string path = argv[1];
	std::ifstream file(path, std::ios::binary);
	if (!file)
	{
		std::cerr << colors::BrightRed << path << ": could not open file" << colors::Reset;
		return 259;
	}

	std::vector<uint8_t> bytecode{
		std::istreambuf_iterator<char>(file),
		std::istreambuf_iterator<char>()
	};

	size_t ip = 0;

	hostFunctions["io.print_int"] = [&]() {
		std::cout << pop(ip);
		};
	hostFunctions["io.print_char"] = [&]() {
		std::cout << static_cast<char>(pop(ip));
		};
	hostFunctions["io.print_newline"] = [&]() {
		std::cout << '\n';
		};

	heapSpace = create_mspace_with_base(heap, HEAP_SIZE, 0);

	while (ip < bytecode.size())
	{
		OpCode instruction = static_cast<OpCode>(bytecode[ip++]);

		switch (instruction)
		{
			case OpCode::HALT:
			{
				uint8_t code = bytecode[ip++];
				if (code == 0)
					std::cout << colors::BrightBlack << "\nprogram exited with code " << static_cast<int>(code);
				else
					std::cerr << colors::BrightRed << "\nunsuccessful exit: " << static_cast<int>(code) << colors::BrightBlack;

				std::chrono::steady_clock::time_point end = std::chrono::steady_clock::now();
				std::cout
					<< "\ntime elapsed: "
					<< std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count()
					<< "ms"
					<< colors::Reset;
				return code;
			}
			case OpCode::PUSH:
			{
				int32_t value = bytecode[ip] | (bytecode[ip + 1] << 8) | (bytecode[ip + 2] << 16) | (bytecode[ip + 3] << 24);
				ip += 4;
				push(value, ip);
				break;
			}
			case OpCode::POP:
			{
				pop(ip);
				break;
			}
			case OpCode::ADD:
			{
				int32_t b = pop(ip);
				int32_t a = pop(ip);
				push(static_cast<int32_t>(static_cast<uint32_t>(a) + static_cast<uint32_t>(b)), ip);
				break;
			}
			case OpCode::SUB:
			{
				int32_t b = pop(ip);
				int32_t a = pop(ip);
				push(static_cast<int32_t>(static_cast<uint32_t>(a) - static_cast<uint32_t>(b)), ip);
				break;
			}
			case OpCode::MUL:
			{
				int32_t b = pop(ip);
				int32_t a = pop(ip);
				push(static_cast<int32_t>(static_cast<uint32_t>(a) * static_cast<uint32_t>(b)), ip);
				break;
			}
			case OpCode::DIV:
			{
				int32_t b = pop(ip);
				int32_t a = pop(ip);
				if (b == 0)
					vmError(ip, "division by zero");
				push(a / b, ip);
				break;
			}
			case OpCode::JUMP:
			{
				int32_t offset = bytecode[ip] | (bytecode[ip + 1] << 8) | (bytecode[ip + 2] << 16) | (bytecode[ip + 3] << 24);
				ip += 4;
				ip += offset;
				break;
			}
			case OpCode::CALL:
			{
				int32_t offset = bytecode[ip] | (bytecode[ip + 1] << 8) | (bytecode[ip + 2] << 16) | (bytecode[ip + 3] << 24);
				ip += 4;
				if (csp >= (int32_t)CALL_STACK_SIZE - 1)
					vmError(ip, "call stack overflow");
				callStack[++csp] = ip;
				ip += offset;
				break;
			}
			case OpCode::RET:
			{
				if (csp < 0)
					vmError(ip, "RET with empty call stack");
				if (fp >= 0) fp--;
				ip = callStack[csp--];
				break;
			}
			case OpCode::JZ:
			{
				int32_t offset = bytecode[ip] | (bytecode[ip + 1] << 8) | (bytecode[ip + 2] << 16) | (bytecode[ip + 3] << 24);
				ip += 4;
				if (pop(ip) == 0)
					ip += offset;
				break;
			}
			case OpCode::JNZ:
			{
				int32_t offset = bytecode[ip] | (bytecode[ip + 1] << 8) | (bytecode[ip + 2] << 16) | (bytecode[ip + 3] << 24);
				ip += 4;
				if (pop(ip) != 0)
					ip += offset;
				break;
			}
			case OpCode::CMP_EQ:
			{
				int32_t b = pop(ip);
				int32_t a = pop(ip);
				push(a == b ? 1 : 0, ip);
				break;
			}
			case OpCode::CMP_LT:
			{
				int32_t b = pop(ip);
				int32_t a = pop(ip);
				push(a < b ? 1 : 0, ip);
				break;
			}
			case OpCode::CMP_GT:
			{
				int32_t b = pop(ip);
				int32_t a = pop(ip);
				push(a > b ? 1 : 0, ip);
				break;
			}
			case OpCode::DUP:
			{
				if (sp < 0)
					vmError(ip, "DUP on empty stack");
				push(stack[sp], ip);
				break;
			}
			case OpCode::ENTER:
			{
				uint8_t count = bytecode[ip++];
				if (fp >= (int32_t)MAX_FRAMES - 1)
					vmError(ip, "frame stack overflow");
				fp++;
				frames[fp].localCount = count;
				memset(frames[fp].locals, 0, count * sizeof(int32_t));
				break;
			}
			case OpCode::LOAD:
			{
				uint8_t slot = bytecode[ip++];
				if (fp < 0) vmError(ip, "LOAD outside of frame");
				if (slot >= frames[fp].localCount) vmError(ip, "local variable index out of range");
				push(frames[fp].locals[slot], ip);
				break;
			}
			case OpCode::STORE:
			{
				uint8_t slot = bytecode[ip++];
				if (fp < 0) vmError(ip, "STORE outside of frame");
				if (slot >= frames[fp].localCount) vmError(ip, "local variable index out of range");
				frames[fp].locals[slot] = pop(ip);
				break;
			}
			case OpCode::ALLOC:
			{
				uint8_t count = bytecode[ip++];
				void* ptr = mspace_malloc(heapSpace, count);
				if (!ptr)
					vmError(ip, "out of heap memory");
				push(static_cast<int32_t>(static_cast<uint8_t*>(ptr) - heap), ip);
				break;
			}
			case OpCode::FREE:
			{
				uint32_t offset = static_cast<uint32_t>(pop(ip));
				mspace_free(heapSpace, heap + offset);
				break;
			}
			case OpCode::LOAD_HEAP:
			{
				uint32_t ptr = static_cast<uint32_t>(pop(ip));
				if (ptr + 4 > HEAP_SIZE)
				{
					std::ostringstream oss;
					oss << "heap read out of bounds at offset 0x" << std::hex << std::setw(8) << std::setfill('0') << ptr;
					vmError(ip, oss.str());
				}
				int32_t value = heap[ptr] | (heap[ptr + 1] << 8) | (heap[ptr + 2] << 16) | (heap[ptr + 3] << 24);
				push(value, ip);
				break;
			}
			case OpCode::STORE_HEAP:
			{
				int32_t value = pop(ip);
				uint32_t ptr = static_cast<uint32_t>(pop(ip));
				if (ptr + 4 > HEAP_SIZE)
				{
					std::ostringstream oss;
					oss << "heap write out of bounds at offset 0x" << std::hex << std::setw(8) << std::setfill('0') << ptr;
					vmError(ip, oss.str());
				}
				heap[ptr] = value & 0xFF;
				heap[ptr + 1] = (value >> 8) & 0xFF;
				heap[ptr + 2] = (value >> 16) & 0xFF;
				heap[ptr + 3] = (value >> 24) & 0xFF;
				break;
			}
			case OpCode::CALL_HOST:
			{
				uint8_t len = bytecode[ip++];
				std::string name(reinterpret_cast<const char*>(&bytecode[ip]), len);
				ip += len;
				auto it = hostFunctions.find(name);
				if (it == hostFunctions.end())
					vmError(ip, "unknown host function '" + name + "'");
				it->second();
				break;
			}
			default:
			{
				std::cerr << colors::BrightRed << "\nunknown instruction: 0x"
					<< std::hex << std::setw(2) << std::setfill('0')
					<< static_cast<int>(bytecode[ip - 1]) << colors::Reset;
				return 256;
			}
		}
	}

	std::cerr << colors::Red << "\nprogram ended without HALT" << colors::Reset;
	return 1;
}