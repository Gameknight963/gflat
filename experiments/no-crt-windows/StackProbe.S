// Windows x64 compiler helper: RAX contains the pending stack allocation size.
// Touch each intervening page so Windows can grow the stack through guard pages.
// Preserve RAX, other registers, and the caller's stack pointer. The caller itself
// subtracts RAX from RSP after this returns. This is deliberately assembly: this
// helper's calling convention is not an ordinary gflat function signature.
.text
.globl __chkstk
.p2align 4
__chkstk:
    pushq %r10
    pushq %r11
    leaq 24(%rsp), %r10
    movq %rax, %r11
.Lnext_page:
    cmpq $4096, %r11
    jbe .Llast_page
    subq $4096, %r10
    testb $0, (%r10)
    subq $4096, %r11
    jmp .Lnext_page
.Llast_page:
    subq %r11, %r10
    testb $0, (%r10)
    popq %r11
    popq %r10
    retq
