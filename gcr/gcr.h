#pragma once
#include <cstdint>
#include <functional>
#include <string>
#include <vector>

void gvm_registerHost(const std::string& name, std::function<void()> fn);
int vm_run(const std::vector<uint8_t>& bytecode, bool optimize = true);
int32_t vm_pop();
void vm_push(int32_t value);