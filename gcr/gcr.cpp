#include "gcr.h"
#include "opcodes.h"
#include "malloc_config.h"
#include "malloc.h"
#include <iostream>
#include <iomanip>
#include <sstream>
#include <cstring>
#include <unordered_map>
#include <functional>
#include <cstdint>
#include <vector>
#include <string>
#include "colors.h"
#include <ios>

static const size_t STACK_SIZE = 4096;
static const size_t CALL_STACK_SIZE = 256;
static const size_t MAX_FRAMES = 64;
static const size_t MAX_LOCALS = 256;
static const size_t HEAP_SIZE = 1024 * 1024;

struct Frame
{
    int32_t locals[MAX_LOCALS];
    int32_t localCount;
};

static int32_t vmStack[STACK_SIZE];
static size_t vmCallStack[CALL_STACK_SIZE];
static uint8_t vmHeap[HEAP_SIZE];
static Frame vmFrames[MAX_FRAMES];

static int32_t sp = -1;
static int32_t csp = -1;
static int32_t fp = -1;
static size_t ip = 0;
static mspace heapSpace;

static std::vector<std::function<void()>> hostFunctionTable;
static std::unordered_map<std::string, uint32_t> hostFunctionIndex;
static std::vector<std::function<void()>> resolvedPool;

static size_t loadPool(const std::vector<uint8_t>& bytecode)
{
    if (bytecode.size() < 4)
    {
        std::cerr << "invalid bytecode: missing pool header\n";
        std::exit(256);
    }

    uint32_t entryCount = bytecode[0] | (bytecode[1] << 8) | (bytecode[2] << 16) | (bytecode[3] << 24);
    size_t i = 4;

    resolvedPool.clear();
    resolvedPool.resize(entryCount, nullptr);

    for (uint32_t e = 0; e < entryCount; e++)
    {
        if (i + 2 > bytecode.size())
        {
            std::cerr << "invalid bytecode: pool entry " << e << " truncated\n";
            std::exit(256);
        }

        uint16_t len = bytecode[i] | (bytecode[i + 1] << 8);
        i += 2;

        if (i + len > bytecode.size())
        {
            std::cerr << "invalid bytecode: pool entry " << e << " data truncated\n";
            std::exit(256);
        }

        std::string name(reinterpret_cast<const char*>(&bytecode[i]), len);
        i += len;

        auto it = hostFunctionIndex.find(name);
        if (it != hostFunctionIndex.end())
            resolvedPool[e] = hostFunctionTable[it->second];
        // if not found, leave as nullptr, not every pool entry has to be a host function
    }

    return i; // bytecode starts here
}

static void vmError(const std::string& message)
{
    std::cerr << colors::BrightRed << "\n" << message << " at byte 0x"
        << std::hex << std::setw(2) << std::setfill('0')
        << (ip - 1) << colors::Reset;
    std::exit(256);
}

void vm_push(int32_t value)
{
    if (sp >= (int32_t)STACK_SIZE - 1)
        vmError("stack overflow");
    vmStack[++sp] = value;
}

int32_t vm_pop()
{
    if (sp < 0)
        vmError("stack underflow");
    return vmStack[sp--];
}

void gvm_registerHost(const std::string& name, std::function<void()> fn)
{
    hostFunctionIndex[name] = static_cast<uint32_t>(hostFunctionTable.size());
    hostFunctionTable.push_back(fn);
}

int vm_run(const std::vector<uint8_t>& bytecode, bool optimize)
{
    ip = loadPool(bytecode);
    sp = -1;
    csp = -1;
    fp = -1;
    heapSpace = create_mspace_with_base(vmHeap, HEAP_SIZE, 0);

    while (ip < bytecode.size())
    {
        OpCode instruction = static_cast<OpCode>(bytecode[ip++]);

        switch (instruction)
        {
            case OpCode::HALT:
            {
                uint8_t code = bytecode[ip++];
                return code;
            }
            case OpCode::PUSH:
            {
                int32_t value = bytecode[ip] | (bytecode[ip + 1] << 8) | (bytecode[ip + 2] << 16) | (bytecode[ip + 3] << 24);
                ip += 4;
                vm_push(value);
                break;
            }
            case OpCode::POP:
            {
                vm_pop();
                break;
            }
            case OpCode::ADD:
            {
                int32_t b = vm_pop();
                int32_t a = vm_pop();
                vm_push(static_cast<int32_t>(static_cast<uint32_t>(a) + static_cast<uint32_t>(b)));
                break;
            }
            case OpCode::SUB:
            {
                int32_t b = vm_pop();
                int32_t a = vm_pop();
                vm_push(static_cast<int32_t>(static_cast<uint32_t>(a) - static_cast<uint32_t>(b)));
                break;
            }
            case OpCode::MUL:
            {
                int32_t b = vm_pop();
                int32_t a = vm_pop();
                vm_push(static_cast<int32_t>(static_cast<uint32_t>(a) * static_cast<uint32_t>(b)));
                break;
            }
            case OpCode::DIV:
            {
                int32_t b = vm_pop();
                int32_t a = vm_pop();
                if (b == 0)
                    vmError("division by zero");
                vm_push(a / b);
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
                    vmError("call stack overflow");
                vmCallStack[++csp] = ip;
                ip += offset;
                break;
            }
            case OpCode::RET:
            {
                if (csp < 0)
                    vmError("RET with empty call stack");
                if (fp >= 0) fp--;
                ip = vmCallStack[csp--];
                break;
            }
            case OpCode::JZ:
            {
                int32_t offset = bytecode[ip] | (bytecode[ip + 1] << 8) | (bytecode[ip + 2] << 16) | (bytecode[ip + 3] << 24);
                ip += 4;
                if (vm_pop() == 0)
                    ip += offset;
                break;
            }
            case OpCode::JNZ:
            {
                int32_t offset = bytecode[ip] | (bytecode[ip + 1] << 8) | (bytecode[ip + 2] << 16) | (bytecode[ip + 3] << 24);
                ip += 4;
                if (vm_pop() != 0)
                    ip += offset;
                break;
            }
            case OpCode::CMP_EQ:
            {
                int32_t b = vm_pop();
                int32_t a = vm_pop();
                vm_push(a == b ? 1 : 0);
                break;
            }
            case OpCode::CMP_LT:
            {
                int32_t b = vm_pop();
                int32_t a = vm_pop();
                vm_push(a < b ? 1 : 0);
                break;
            }
            case OpCode::CMP_GT:
            {
                int32_t b = vm_pop();
                int32_t a = vm_pop();
                vm_push(a > b ? 1 : 0);
                break;
            }
            case OpCode::DUP:
            {
                if (sp < 0)
                    vmError("DUP on empty stack");
                vm_push(vmStack[sp]);
                break;
            }
            case OpCode::ENTER:
            {
                uint8_t count = bytecode[ip++];
                if (fp >= (int32_t)MAX_FRAMES - 1)
                    vmError("frame stack overflow");
                fp++;
                vmFrames[fp].localCount = count;
                memset(vmFrames[fp].locals, 0, count * sizeof(int32_t));
                break;
            }
            case OpCode::LOAD:
            {
                uint8_t slot = bytecode[ip++];
                if (fp < 0) vmError("LOAD outside of frame");
                if (slot >= (size_t)vmFrames[fp].localCount) vmError("local variable index out of range");
                vm_push(vmFrames[fp].locals[slot]);
                break;
            }
            case OpCode::STORE:
            {
                uint8_t slot = bytecode[ip++];
                if (fp < 0) vmError("STORE outside of frame");
                if (slot >= (size_t)vmFrames[fp].localCount) vmError("local variable index out of range");
                vmFrames[fp].locals[slot] = vm_pop();
                break;
            }
            case OpCode::ALLOC:
            {
                uint8_t count = bytecode[ip++];
                void* ptr = mspace_malloc(heapSpace, count);
                if (!ptr)
                    vmError("out of heap memory");
                vm_push(static_cast<int32_t>(static_cast<uint8_t*>(ptr) - vmHeap));
                break;
            }
            case OpCode::FREE:
            {
                uint32_t offset = static_cast<uint32_t>(vm_pop());
                mspace_free(heapSpace, vmHeap + offset);
                break;
            }
            case OpCode::LOAD_HEAP:
            {
                uint32_t ptr = static_cast<uint32_t>(vm_pop());
                if (ptr + 4 > HEAP_SIZE)
                {
                    std::ostringstream oss;
                    oss << "heap read out of bounds at offset 0x" << std::hex << std::setw(8) << std::setfill('0') << ptr;
                    vmError(oss.str());
                }
                int32_t value = vmHeap[ptr] | (vmHeap[ptr + 1] << 8) | (vmHeap[ptr + 2] << 16) | (vmHeap[ptr + 3] << 24);
                vm_push(value);
                break;
            }
            case OpCode::STORE_HEAP:
            {
                int32_t value = vm_pop();
                uint32_t ptr = static_cast<uint32_t>(vm_pop());
                if (ptr + 4 > HEAP_SIZE)
                {
                    std::ostringstream oss;
                    oss << "heap write out of bounds at offset 0x" << std::hex << std::setw(8) << std::setfill('0') << ptr;
                    vmError(oss.str());
                }
                vmHeap[ptr] = value & 0xFF;
                vmHeap[ptr + 1] = (value >> 8) & 0xFF;
                vmHeap[ptr + 2] = (value >> 16) & 0xFF;
                vmHeap[ptr + 3] = (value >> 24) & 0xFF;
                break;
            }
            case OpCode::CALL_HOST:
            {
                uint32_t index = bytecode[ip] | (bytecode[ip + 1] << 8) | (bytecode[ip + 2] << 16) | (bytecode[ip + 3] << 24);
                ip += 4;
                if (index >= resolvedPool.size() || resolvedPool[index] == nullptr)
                    vmError("invalid or unresolved pool entry " + std::to_string(index));
                resolvedPool[index]();
                break;
            }
            case OpCode::NOP:
            {
                // nothing to do
                break;
            }
            default:
            {
                std::ostringstream oss;
                oss << "unknown instruction: 0x" << std::hex << std::setw(2) << std::setfill('0') << static_cast<int>(bytecode[ip - 1]);
                vmError(oss.str());
            }
        }
    }

    vmError("program ended without HALT");
    return 1;
}