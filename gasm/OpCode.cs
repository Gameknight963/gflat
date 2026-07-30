namespace gssembler
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
        PRINT = 0x07,
    }
}
