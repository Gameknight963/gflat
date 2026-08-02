#include "gcr.h"
#include "opcodes.h"
#include <iostream>
#include <iomanip>
#include <sstream>
#include <cstring>

void GVM::vmError(const std::string& message)
{
    std::cerr << "\n" << message << " at byte 0x"
        << std::hex << std::setw(2) << std::setfill('0')
        << (ip - 1);
    std::exit(256);
}

void GVM::push(int32_t value)
{
    if (sp >= (int32_t)STACK_SIZE - 1)
        vmError("stack overflow");
    stack[++sp] = value;
}

int32_t GVM::pop()
{
    if (sp < 0)
        vmError("stack underflow");
    return stack[sp--];
}

void GVM::registerHost(const std::string& name, std::function<void()> fn)
{
    hostFunctions[name] = fn;
}

int GVM::run(const std::vector<uint8_t>& bytecode)
{
    ip = 0;
    sp = -1;
    csp = -1;
    fp = -1;
    heapSpace = create_mspace_with_base(heap, HEAP_SIZE, 0);

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
                push(value);
                break;
            }
            case OpCode::POP:
            {
                pop();
                break;
            }
            case OpCode::ADD:
            {
                int32_t b = pop();
                int32_t a = pop();
                push(static_cast<int32_t>(static_cast<uint32_t>(a) + static_cast<uint32_t>(b)));
                break;
            }
            case OpCode::SUB:
            {
                int32_t b = pop();
                int32_t a = pop();
                push(static_cast<int32_t>(static_cast<uint32_t>(a) - static_cast<uint32_t>(b)));
                break;
            }
            case OpCode::MUL:
            {
                int32_t b = pop();
                int32_t a = pop();
                push(static_cast<int32_t>(static_cast<uint32_t>(a) * static_cast<uint32_t>(b)));
                break;
            }
            case OpCode::DIV:
            {
                int32_t b = pop();
                int32_t a = pop();
                if (b == 0)
                    vmError("division by zero");
                push(a / b);
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
                callStack[++csp] = ip;
                ip += offset;
                break;
            }
            case OpCode::RET:
            {
                if (csp < 0)
                    vmError("RET with empty call stack");
                if (fp >= 0) fp--;
                ip = callStack[csp--];
                break;
            }
            case OpCode::JZ:
            {
                int32_t offset = bytecode[ip] | (bytecode[ip + 1] << 8) | (bytecode[ip + 2] << 16) | (bytecode[ip + 3] << 24);
                ip += 4;
                if (pop() == 0)
                    ip += offset;
                break;
            }
            case OpCode::JNZ:
            {
                int32_t offset = bytecode[ip] | (bytecode[ip + 1] << 8) | (bytecode[ip + 2] << 16) | (bytecode[ip + 3] << 24);
                ip += 4;
                if (pop() != 0)
                    ip += offset;
                break;
            }
            case OpCode::CMP_EQ:
            {
                int32_t b = pop();
                int32_t a = pop();
                push(a == b ? 1 : 0);
                break;
            }
            case OpCode::CMP_LT:
            {
                int32_t b = pop();
                int32_t a = pop();
                push(a < b ? 1 : 0);
                break;
            }
            case OpCode::CMP_GT:
            {
                int32_t b = pop();
                int32_t a = pop();
                push(a > b ? 1 : 0);
                break;
            }
            case OpCode::DUP:
            {
                if (sp < 0)
                    vmError("DUP on empty stack");
                push(stack[sp]);
                break;
            }
            case OpCode::ENTER:
            {
                uint8_t count = bytecode[ip++];
                if (fp >= (int32_t)MAX_FRAMES - 1)
                    vmError("frame stack overflow");
                fp++;
                frames[fp].localCount = count;
                memset(frames[fp].locals, 0, count * sizeof(int32_t));
                break;
            }
            case OpCode::LOAD:
            {
                uint8_t slot = bytecode[ip++];
                if (fp < 0) vmError("LOAD outside of frame");
                if (slot >= frames[fp].localCount) vmError("local variable index out of range");
                push(frames[fp].locals[slot]);
                break;
            }
            case OpCode::STORE:
            {
                uint8_t slot = bytecode[ip++];
                if (fp < 0) vmError("STORE outside of frame");
                if (slot >= frames[fp].localCount) vmError("local variable index out of range");
                frames[fp].locals[slot] = pop();
                break;
            }
            case OpCode::ALLOC:
            {
                uint8_t count = bytecode[ip++];
                void* ptr = mspace_malloc(heapSpace, count);
                if (!ptr)
                    vmError("out of heap memory");
                push(static_cast<int32_t>(static_cast<uint8_t*>(ptr) - heap));
                break;
            }
            case OpCode::FREE:
            {
                uint32_t offset = static_cast<uint32_t>(pop());
                mspace_free(heapSpace, heap + offset);
                break;
            }
            case OpCode::LOAD_HEAP:
            {
                uint32_t ptr = static_cast<uint32_t>(pop());
                if (ptr + 4 > HEAP_SIZE)
                {
                    std::ostringstream oss;
                    oss << "heap read out of bounds at offset 0x" << std::hex << std::setw(8) << std::setfill('0') << ptr;
                    vmError(oss.str());
                }
                int32_t value = heap[ptr] | (heap[ptr + 1] << 8) | (heap[ptr + 2] << 16) | (heap[ptr + 3] << 24);
                push(value);
                break;
            }
            case OpCode::STORE_HEAP:
            {
                int32_t value = pop();
                uint32_t ptr = static_cast<uint32_t>(pop());
                if (ptr + 4 > HEAP_SIZE)
                {
                    std::ostringstream oss;
                    oss << "heap write out of bounds at offset 0x" << std::hex << std::setw(8) << std::setfill('0') << ptr;
                    vmError(oss.str());
                }
                heap[ptr]     = value & 0xFF;
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
                    vmError("unknown host function '" + name + "'");
                it->second();
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