#include <stddef.h>
#include <stdint.h>

static inline uint64_t read_ctr_el0(void)
{
    uint64_t value;
    __asm__ volatile("mrs %0, ctr_el0" : "=r"(value));
    return value;
}

static inline uint64_t read_cntfrq_el0(void)
{
    uint64_t value;
    __asm__ volatile("mrs %0, cntfrq_el0" : "=r"(value));
    return value;
}

static inline uint64_t read_cntvct_el0(void)
{
    uint64_t value;
    __asm__ volatile("mrs %0, cntvct_el0" : "=r"(value));
    return value;
}

static inline uintptr_t align_down(uintptr_t value, uintptr_t alignment)
{
    return value & ~(alignment - 1u);
}

static inline uintptr_t align_up(uintptr_t value, uintptr_t alignment)
{
    return (value + alignment - 1u) & ~(alignment - 1u);
}

static inline uintptr_t data_cache_line_size(void)
{
    /* CTR_EL0.DminLine is log2(words) for the smallest D-cache line. */
    uint64_t ctr = read_ctr_el0();
    uint64_t dminline = (ctr >> 16) & 0xFu;
    return (uintptr_t)(4u << dminline);
}

void _zonderq_arm64_dma_clean(void *address, uint64_t length)
{
    if (address == NULL || length == 0)
        return;

    uintptr_t line = data_cache_line_size();
    uintptr_t start = align_down((uintptr_t)address, line);
    uintptr_t end = align_up((uintptr_t)address + (uintptr_t)length, line);

    for (uintptr_t p = start; p < end; p += line)
        __asm__ volatile("dc cvac, %0" :: "r"(p) : "memory");

    __asm__ volatile("dsb sy" ::: "memory");
}

void _zonderq_arm64_dma_invalidate(void *address, uint64_t length)
{
    if (address == NULL || length == 0)
        return;

    uintptr_t line = data_cache_line_size();
    uintptr_t start = align_down((uintptr_t)address, line);
    uintptr_t end = align_up((uintptr_t)address + (uintptr_t)length, line);

    for (uintptr_t p = start; p < end; p += line)
        __asm__ volatile("dc ivac, %0" :: "r"(p) : "memory");

    __asm__ volatile("dsb sy" ::: "memory");
    __asm__ volatile("isb" ::: "memory");
}

void _zonderq_arm64_dma_clean_invalidate(void *address, uint64_t length)
{
    if (address == NULL || length == 0)
        return;

    uintptr_t line = data_cache_line_size();
    uintptr_t start = align_down((uintptr_t)address, line);
    uintptr_t end = align_up((uintptr_t)address + (uintptr_t)length, line);

    for (uintptr_t p = start; p < end; p += line)
        __asm__ volatile("dc civac, %0" :: "r"(p) : "memory");

    __asm__ volatile("dsb sy" ::: "memory");
    __asm__ volatile("isb" ::: "memory");
}

void _zonderq_arm64_delay_us(uint64_t microseconds)
{
    uint64_t frequency = read_cntfrq_el0();
    if (frequency == 0 || microseconds == 0)
        return;

    uint64_t start = read_cntvct_el0();
    uint64_t ticks = (frequency * microseconds + 999999u) / 1000000u;
    uint64_t target = start + ticks;

    while ((int64_t)(read_cntvct_el0() - target) < 0)
        __asm__ volatile("yield");
}
