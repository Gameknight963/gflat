#pragma once
#pragma once
#include <cstdint>
#include <functional>
#include <string>
#include <unordered_map>
#include <vector>
#include "malloc_config.h"
#include "malloc.h"

class GVM
{
public:
    void registerHost(const std::string& name, std::function<void()> fn);
    int run(const std::vector<uint8_t>& bytecode);

    int32_t pop();
    void push(int32_t value);

private:
    static const size_t STACK_SIZE = 4096;
    static const size_t CALL_STACK_SIZE = 256;
    static const size_t MAX_FRAMES = 64;
    static const size_t MAX_LOCALS = 256;
    static const size_t HEAP_SIZE = 1024 * 1024;

    int32_t stack[STACK_SIZE];
    int32_t sp = -1;

    size_t callStack[CALL_STACK_SIZE];
    int32_t csp = -1;

    uint8_t heap[HEAP_SIZE];
    mspace heapSpace;

    struct Frame
    {
        int32_t locals[MAX_LOCALS];
        int32_t localCount;
    };

    Frame frames[MAX_FRAMES];
    int32_t fp = -1;

    size_t ip = 0;

    std::unordered_map<std::string, std::function<void()>> hostFunctions;

    void vmError(const std::string& message);
};