#ifndef ZONDERQ_BOOTGUARD_ELF_H
#define ZONDERQ_BOOTGUARD_ELF_H

/*
 * Pure freestanding ELF64 x86_64 header and program-header validator for the
 * EFI boot stage. `header` is exactly the first 64 bytes of the file.
 * `file_length` is obtained independently from the firmware filesystem, never
 * from the header.
 *
 * This guards against obviously corrupt/non-bootable images. It is NOT a
 * cryptographic signature or a complete ELF loader. Package authenticity
 * must be checked by the signed release updater in the running kernel.
 */
static inline unsigned int zq_elf64_read_u16(const unsigned char *p)
{
    return (unsigned int)p[0] | ((unsigned int)p[1] << 8);
}

static inline unsigned int zq_elf64_read_u32(const unsigned char *p)
{
    return (unsigned int)p[0] |
           ((unsigned int)p[1] << 8) |
           ((unsigned int)p[2] << 16) |
           ((unsigned int)p[3] << 24);
}

static inline unsigned long long zq_elf64_read_u64(const unsigned char *p)
{
    unsigned long long v = 0;
    for (unsigned int i = 0; i < 8u; ++i)
        v |= ((unsigned long long)p[i]) << (8u * i);
    return v;
}

static inline int zq_elf64_x64_header_valid(const unsigned char *h,
                                            unsigned long long file_length)
{
    if (!h || file_length < 120u)
        return 0;
    if (h[0] != 0x7fu || h[1] != 'E' || h[2] != 'L' || h[3] != 'F' ||
        h[4] != 2u || h[5] != 1u || h[6] != 1u)
        return 0;
    unsigned int type = zq_elf64_read_u16(h + 16);
    unsigned int machine = zq_elf64_read_u16(h + 18);
    if ((type != 2u && type != 3u) || machine != 62u)
        return 0;

    unsigned long long phoff = zq_elf64_read_u64(h + 32);
    unsigned int phentsize = zq_elf64_read_u16(h + 54);
    unsigned int phnum = zq_elf64_read_u16(h + 56);
    if (phoff < 64u || phentsize < 56u || phnum == 0u || phoff > file_length)
        return 0;

    /* Division avoids overflow in phoff + phnum * phentsize. */
    return (unsigned long long)phnum <=
        (file_length - phoff) / (unsigned long long)phentsize;
}

/*
 * Validate one 56-byte ELF64 program header (`Elf64_Phdr`) against the
 * filesystem-reported `file_length`. Enforces strict bounds on PT_LOAD (and
 * any file-backed segment) to catch truncated or malformed ELF payloads.
 */
static inline int zq_elf64_x64_phdr_valid(const unsigned char *phdr,
                                          unsigned long long file_length)
{
    if (!phdr)
        return 0;
    unsigned int p_type = zq_elf64_read_u32(phdr + 0);
    unsigned long long p_offset = zq_elf64_read_u64(phdr + 8);
    unsigned long long p_filesz = zq_elf64_read_u64(phdr + 32);
    unsigned long long p_memsz = zq_elf64_read_u64(phdr + 40);
    unsigned long long p_align = zq_elf64_read_u64(phdr + 48);

    if (p_type == 1u) /* PT_LOAD */
    {
        if (p_filesz > p_memsz)
            return 0;
        if (p_offset > file_length || p_filesz > file_length - p_offset)
            return 0;
        if (p_align > 1u && (p_align & (p_align - 1u)) != 0u)
            return 0;
    }
    else if (p_type != 0u && p_filesz > 0u)
    {
        if (p_offset > file_length || p_filesz > file_length - p_offset)
            return 0;
    }
    return 1;
}

#endif
