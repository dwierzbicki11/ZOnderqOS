typedef unsigned long ulong_t;
typedef unsigned long long u64;

#define ZONDERQ_DMA_ARENA_SIZE (512UL * 1024UL)

__attribute__((aligned(4096)))
static unsigned char g_zonderq_dma_arena[ZONDERQ_DMA_ARENA_SIZE];

void* zonderq_dma_arena_base(void)
{
    return (void*)g_zonderq_dma_arena;
}

u64 zonderq_dma_arena_size(void)
{
    return (u64)ZONDERQ_DMA_ARENA_SIZE;
}

static inline ulong_t cache_line_bytes(void)
{
    ulong_t ctr;
    __asm__ volatile("mrs %0, ctr_el0" : "=r"(ctr));
    return 4UL << ((ctr >> 16) & 0xFUL);
}

static inline ulong_t align_down(ulong_t value, ulong_t alignment)
{
    return value & ~(alignment - 1UL);
}

void zonderq_dma_clean(void* address, u64 length)
{
    if (address == (void*)0 || length == 0)
        return;

    ulong_t line = cache_line_bytes();
    ulong_t start = align_down((ulong_t)address, line);
    ulong_t end = (ulong_t)address + (ulong_t)length;

    for (ulong_t p = start; p < end; p += line)
        __asm__ volatile("dc cvac, %0" :: "r"(p) : "memory");

    __asm__ volatile("dsb sy" ::: "memory");
}

void zonderq_dma_invalidate(void* address, u64 length)
{
    if (address == (void*)0 || length == 0)
        return;

    ulong_t line = cache_line_bytes();
    ulong_t start = align_down((ulong_t)address, line);
    ulong_t end = (ulong_t)address + (ulong_t)length;

    for (ulong_t p = start; p < end; p += line)
        __asm__ volatile("dc ivac, %0" :: "r"(p) : "memory");

    __asm__ volatile("dsb sy" ::: "memory");
}

void zonderq_dma_clean_invalidate(void* address, u64 length)
{
    if (address == (void*)0 || length == 0)
        return;

    ulong_t line = cache_line_bytes();
    ulong_t start = align_down((ulong_t)address, line);
    ulong_t end = (ulong_t)address + (ulong_t)length;

    for (ulong_t p = start; p < end; p += line)
        __asm__ volatile("dc civac, %0" :: "r"(p) : "memory");

    __asm__ volatile("dsb sy" ::: "memory");
}
