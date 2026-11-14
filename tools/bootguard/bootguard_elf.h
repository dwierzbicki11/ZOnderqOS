#ifndef ZONDERQ_BOOTGUARD_ELF_H
#define ZONDERQ_BOOTGUARD_ELF_H

/*
 * Pure freestanding ELF64 x86_64 header validator for the EFI boot stage.
 * `header` is exactly the first 64 bytes of the file. `file_length` is
 * obtained independently from the firmware filesystem, never from the header.
 *
 * This guards against obviously corrupt/non-bootable images. It is NOT a
 * cryptographic signature or a complete ELF loader. Package authenticity
 * must be checked by the signed release updater in the running kernel.
 */
static int zq_elf64_x64_header_valid(const unsigned char *h,
                                     unsigned long long file_length)
{
    if (!h || file_length < 120u)
        return 0;
    if (h[0] != 0x7fu || h[1] != 'E' || h[2] != 'L' || h[3] != 'F' ||
        h[4] != 2u || h[5] != 1u || h[6] != 1u)
        return 0;
    unsigned int type = (unsigned int)h[16] | ((unsigned int)h[17] << 8);
    unsigned int machine = (unsigned int)h[18] | ((unsigned int)h[19] << 8);
    if ((type != 2u && type != 3u) || machine != 62u)
        return 0;

    unsigned long long phoff = 0;
    for (unsigned int i = 0; i < 8u; ++i)
        phoff |= ((unsigned long long)h[32u+i]) << (8u*i);

    unsigned int phentsize = (unsigned int)h[54] | ((unsigned int)h[55] << 8);
    unsigned int phnum = (unsigned int)h[56] | ((unsigned int)h[57] << 8);
    if (phoff < 64u || phentsize < 56u || phnum == 0u || phoff > file_length)
        return 0;

    /* Division avoids overflow in phoff + phnum * phentsize. */
    return (unsigned long long)phnum <=
        (file_length - phoff) / (unsigned long long)phentsize;
}

#endif
