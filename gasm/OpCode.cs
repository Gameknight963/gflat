namespace gasm
{
    enum OpCode : byte
    {
        HALT = 0x00,
        PUSH = 0x01,
        POP = 0x02,
        ADD = 0x03,
        SUB = 0x04,
        MUL = 0x05,
        DIV = 0x06,
        JUMP = 0x07,
        CALL = 0x08,
        RET = 0x09,
        JZ = 0x0A,
        JNZ = 0x0B,
        CMP_EQ = 0x0C,
        CMP_LT = 0x0D,
        CMP_GT = 0x0E,
        DUP = 0x0F,
        CALL_HOST = 0x10,
        ENTER = 0x11,
        LOAD = 0x12,
        STORE = 0x13,
        ALLOC = 0x14,
        FREE = 0x15,
        LOAD_HEAP = 0x16,
        STORE_HEAP = 0x17,
        NOP = 0x18,
    }
}
