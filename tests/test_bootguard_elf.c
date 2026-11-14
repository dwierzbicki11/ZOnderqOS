#include <stdio.h>
#include <string.h>
#include "../tools/bootguard/bootguard_elf.h"
static unsigned checked;
#define CHECK(expr) do { ++checked; if (!(expr)) { \
    fprintf(stderr, "FAIL %d: %s\n", __LINE__, #expr); return 1; \
} } while (0)
int main(void)
{
    unsigned char h[64] = {0x7f,'E','L','F',2,1,1};
    h[16] = 2; h[18] = 62;
    h[32] = 64; h[54] = 56; h[56] = 1;
    CHECK(zq_elf64_x64_header_valid(h, 120));
    CHECK(zq_elf64_x64_header_valid(h, 128));
    CHECK(!zq_elf64_x64_header_valid(h, 119));
    CHECK(!zq_elf64_x64_header_valid(h, 64));
    CHECK(!zq_elf64_x64_header_valid(0, 128));
    h[0] = 0; CHECK(!zq_elf64_x64_header_valid(h, 128)); h[0] = 0x7f;
    h[4] = 1; CHECK(!zq_elf64_x64_header_valid(h, 128)); h[4] = 2;
    h[5] = 2; CHECK(!zq_elf64_x64_header_valid(h, 128)); h[5] = 1;
    h[18] = 183; CHECK(!zq_elf64_x64_header_valid(h, 128)); h[18] = 62;
    h[16] = 0; CHECK(!zq_elf64_x64_header_valid(h, 128)); h[16] = 2;
    h[54] = 0; CHECK(!zq_elf64_x64_header_valid(h, 128)); h[54] = 56;
    h[56] = 0; CHECK(!zq_elf64_x64_header_valid(h, 128)); h[56] = 1;
    h[32] = 120; CHECK(!zq_elf64_x64_header_valid(h, 128)); h[32] = 64;
    h[32] = 0xff; h[33] = 0xff; h[34] = 0xff; h[35] = 0xff;
    h[36] = 0xff; h[37] = 0xff; h[38] = 0xff; h[39] = 0xff;
    CHECK(!zq_elf64_x64_header_valid(h, 128));
    puts("BOOTGUARD ELF64 HEADER VALIDATION PASS");
    return 0;
}
