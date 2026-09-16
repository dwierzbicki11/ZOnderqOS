typedef unsigned char u8;
typedef unsigned short u16;
typedef unsigned int u32;
typedef unsigned long long u64;
typedef signed int s32;

#define PCIE_REG_PHYS              0x00000000FD500000ULL
#define PCIE_CPU_MMIO_BASE         0x0000000600000000ULL
#define PCIE_BUS_MMIO_BASE         0x00000000F8000000ULL
#define PCIE_BUS_MMIO_LENGTH       0x0000000004000000ULL
#define PCIE_CFG_DATA_OFF          0x8000ULL
#define PCIE_CFG_INDEX_OFF         0x9000ULL

#define PCI_VENDOR_ID_OFF          0x00ULL
#define PCI_COMMAND_OFF            0x04ULL
#define PCI_CLASS_DWORD_OFF        0x08ULL
#define PCI_BAR0_OFF               0x10ULL
#define PCI_SECONDARY_BUS_OFF      0x19ULL
#define PCI_COMMAND_MEMORY         0x0002U

#define MAILBOX_PHYS               0x00000000FE00B880ULL
#define MAIL0_READ_OFF             0x00ULL
#define MAIL0_STATUS_OFF           0x18ULL
#define MAIL1_WRITE_OFF            0x20ULL
#define MAIL1_STATUS_OFF           0x38ULL
#define MAILBOX_FULL               0x80000000U
#define MAILBOX_EMPTY              0x40000000U
#define MAILBOX_PROPERTY_CHANNEL   8U
#define MAILBOX_GPU_UNCACHED_BASE  0xC0000000U
#define FW_STATUS_SUCCESS          0x80000000U
#define FW_NOTIFY_XHCI_RESET       0x00030058U
#define VL805_FIRMWARE_BDF         0x00100000U

#define XHCI_HCCPARAMS1_OFF        0x10ULL
#define XHCI_USBCMD_OFF            0x00ULL
#define XHCI_USBSTS_OFF            0x04ULL
#define XHCI_PAGESIZE_OFF          0x08ULL
#define XHCI_CONFIG_OFF            0x38ULL
#define XHCI_CMD_RUN               (1U << 0)
#define XHCI_CMD_HCRST             (1U << 1)
#define XHCI_STS_HCH               (1U << 0)
#define XHCI_STS_CNR               (1U << 11)
#define XHCI_STS_HCE               (1U << 12)

#define XHCI_EXTCAP_LEGACY         1U
#define XHCI_LEGACY_BIOS_OWNED     (1U << 16)
#define XHCI_LEGACY_OS_OWNED       (1U << 24)

#define RESULT_WORDS               32U
#define R_MAGIC                    0U
#define R_STAGE                    1U
#define R_ERROR                    2U
#define R_RETRY                    3U
#define R_MBOX_BUF_PHYS            4U
#define R_MBOX_MESSAGE             5U
#define R_MBOX_REPLY               6U
#define R_MBOX_STATUS              7U
#define R_MBOX_TAG_STATUS          8U
#define R_SECONDARY_BUS            9U
#define R_XHCI_PHYS               10U
#define R_CAPABILITY0              11U
#define R_PCI_COMMAND_BEFORE       12U
#define R_PCI_COMMAND_AFTER        13U
#define R_LEGSUP_BEFORE            14U
#define R_LEGSUP_AFTER             15U
#define R_USBCMD_BEFORE            16U
#define R_USBSTS_BEFORE            17U
#define R_USBCMD_AFTER             18U
#define R_USBSTS_AFTER             19U
#define R_PAGESIZE                 20U
#define R_CONFIG                   21U
#define R_LAST_USBCMD              22U
#define R_LAST_USBSTS              23U
#define R_MAILBOX_ATTEMPTS         24U

#define ERR_OK                     0
#define ERR_BAD_ARGUMENT          -1
#define ERR_SECONDARY_BUS         -2
#define ERR_PCI_ENDPOINT          -3
#define ERR_PCI_BAR               -4
#define ERR_XHCI_SIGNATURE        -5
#define ERR_MBOX_VA_TO_PA         -6
#define ERR_MBOX_ADDRESS_RANGE    -7
#define ERR_MBOX_TX_TIMEOUT       -8
#define ERR_MBOX_RX_TIMEOUT       -9
#define ERR_MBOX_RESPONSE         -10
#define ERR_LEGACY_TIMEOUT        -11
#define ERR_LEGACY_OWNERSHIP      -12
#define ERR_HCE_BEFORE            -13
#define ERR_HALT_TIMEOUT          -14
#define ERR_HCRST_TIMEOUT         -15
#define ERR_CNR_TIMEOUT           -16
#define ERR_HCE_AFTER             -17
#define ERR_NOT_HALTED            -18
#define ERR_PAGE_SIZE             -19

/* One full page guarantees the mailbox property packet never crosses a page. */
static volatile u32 g_property_page[1024] __attribute__((aligned(4096)));

static inline void barrier_full(void)
{
    __asm__ volatile("dsb sy\n\tisb" ::: "memory");
}

static inline u64 timer_frequency(void)
{
    u64 value;
    __asm__ volatile("mrs %0, cntfrq_el0" : "=r"(value));
    return value;
}

static inline u64 timer_counter(void)
{
    u64 value;
    __asm__ volatile("mrs %0, cntpct_el0" : "=r"(value));
    return value;
}

static inline u64 timeout_ticks(u64 microseconds)
{
    u64 frequency = timer_frequency();
    if (frequency == 0)
        return 1;
    return (frequency * microseconds + 999999ULL) / 1000000ULL;
}

static void delay_us(u64 microseconds)
{
    u64 start = timer_counter();
    u64 ticks = timeout_ticks(microseconds);
    while ((timer_counter() - start) < ticks)
        __asm__ volatile("yield");
}

static inline u8 mmio_read8(u64 address)
{
    return *(volatile u8 *)(unsigned long)address;
}

static inline u16 mmio_read16(u64 address)
{
    return *(volatile u16 *)(unsigned long)address;
}

static inline u32 mmio_read32(u64 address)
{
    return *(volatile u32 *)(unsigned long)address;
}

static inline void mmio_write16(u64 address, u16 value)
{
    *(volatile u16 *)(unsigned long)address = value;
    barrier_full();
}

static inline void mmio_write32(u64 address, u32 value)
{
    *(volatile u32 *)(unsigned long)address = value;
    barrier_full();
}

static inline u32 dcache_line_size(void)
{
    u64 ctr;
    __asm__ volatile("mrs %0, ctr_el0" : "=r"(ctr));
    return 4U << ((ctr >> 16) & 0xFU);
}

static void cache_clean_range(const void *pointer, u64 length)
{
    u64 line = dcache_line_size();
    u64 start = ((u64)(unsigned long)pointer) & ~(line - 1ULL);
    u64 end = ((u64)(unsigned long)pointer) + length;
    u64 address;

    for (address = start; address < end; address += line)
        __asm__ volatile("dc cvac, %0" :: "r"(address) : "memory");

    __asm__ volatile("dsb sy" ::: "memory");
}

static void cache_invalidate_range(const void *pointer, u64 length)
{
    u64 line = dcache_line_size();
    u64 start = ((u64)(unsigned long)pointer) & ~(line - 1ULL);
    u64 end = ((u64)(unsigned long)pointer) + length;
    u64 address;

    __asm__ volatile("dsb sy" ::: "memory");
    for (address = start; address < end; address += line)
        __asm__ volatile("dc ivac, %0" :: "r"(address) : "memory");
    barrier_full();
}

static int virtual_to_physical(u64 virtual_address, u64 *physical_address)
{
    u64 par;

    __asm__ volatile("at s1e1r, %0" :: "r"(virtual_address) : "memory");
    __asm__ volatile("isb" ::: "memory");
    __asm__ volatile("mrs %0, par_el1" : "=r"(par));

    if ((par & 1ULL) != 0)
        return 0;

    *physical_address = (par & 0x0000FFFFFFFFF000ULL) | (virtual_address & 0xFFFULL);
    return 1;
}

static int wait_register_bits(u64 address, u32 mask, u32 wanted, u64 timeout_us_value, u32 *last)
{
    u64 start = timer_counter();
    u64 ticks = timeout_ticks(timeout_us_value);

    do
    {
        u32 value = mmio_read32(address);
        *last = value;
        if ((value & mask) == wanted)
            return 1;
        __asm__ volatile("yield");
    }
    while ((timer_counter() - start) < ticks);

    *last = mmio_read32(address);
    return ((*last & mask) == wanted);
}

static int locate_xhci(u64 hhdm, u64 *xhci_base, u64 *operational_base, u64 *result)
{
    u64 pcie = hhdm + PCIE_REG_PHYS;
    u8 secondary_bus = mmio_read8(pcie + PCI_SECONDARY_BUS_OFF);
    u64 config;
    u16 vendor;
    u32 class_dword;
    u32 bar_low;
    u32 bar_type;
    u64 pci_bar;
    u16 command_before;
    u16 command_after;
    u64 xhci_physical;
    u32 capability0;
    u8 cap_length;
    u16 hci_version;

    result[R_SECONDARY_BUS] = secondary_bus;
    if (secondary_bus == 0)
        return ERR_SECONDARY_BUS;

    mmio_write32(pcie + PCIE_CFG_INDEX_OFF, ((u32)secondary_bus) << 20);
    config = pcie + PCIE_CFG_DATA_OFF;

    vendor = mmio_read16(config + PCI_VENDOR_ID_OFF);
    class_dword = mmio_read32(config + PCI_CLASS_DWORD_OFF);
    if (vendor == 0 || vendor == 0xFFFFU ||
        ((class_dword >> 24) & 0xFFU) != 0x0CU ||
        ((class_dword >> 16) & 0xFFU) != 0x03U ||
        ((class_dword >> 8) & 0xFFU) != 0x30U)
        return ERR_PCI_ENDPOINT;

    bar_low = mmio_read32(config + PCI_BAR0_OFF);
    if ((bar_low & 1U) != 0)
        return ERR_PCI_BAR;

    bar_type = (bar_low >> 1) & 3U;
    if (bar_type == 3U)
        return ERR_PCI_BAR;

    pci_bar = (u64)(bar_low & 0xFFFFFFF0U);
    if (bar_type == 2U)
        pci_bar |= ((u64)mmio_read32(config + PCI_BAR0_OFF + 4ULL)) << 32;

    if (pci_bar < PCIE_BUS_MMIO_BASE || pci_bar >= (PCIE_BUS_MMIO_BASE + PCIE_BUS_MMIO_LENGTH))
        return ERR_PCI_BAR;

    command_before = mmio_read16(config + PCI_COMMAND_OFF);
    command_after = command_before;
    if ((command_after & PCI_COMMAND_MEMORY) == 0)
    {
        command_after = (u16)(command_after | PCI_COMMAND_MEMORY);
        mmio_write16(config + PCI_COMMAND_OFF, command_after);
        command_after = mmio_read16(config + PCI_COMMAND_OFF);
    }

    result[R_PCI_COMMAND_BEFORE] = command_before;
    result[R_PCI_COMMAND_AFTER] = command_after;
    if ((command_after & PCI_COMMAND_MEMORY) == 0)
        return ERR_PCI_ENDPOINT;

    xhci_physical = PCIE_CPU_MMIO_BASE + (pci_bar - PCIE_BUS_MMIO_BASE);
    *xhci_base = hhdm + xhci_physical;
    result[R_XHCI_PHYS] = xhci_physical;

    capability0 = mmio_read32(*xhci_base);
    result[R_CAPABILITY0] = capability0;
    cap_length = (u8)(capability0 & 0xFFU);
    hci_version = (u16)(capability0 >> 16);

    if (cap_length < 0x20U || cap_length > 0x80U || hci_version < 0x0090U || hci_version > 0x0200U)
        return ERR_XHCI_SIGNATURE;

    *operational_base = *xhci_base + (u64)cap_length;
    return ERR_OK;
}

static int firmware_notify_xhci_reset(u64 hhdm, u64 *result)
{
    volatile u32 *buffer = g_property_page;
    u64 buffer_virtual = (u64)(unsigned long)buffer;
    u64 buffer_physical = 0;
    u32 bus_address;
    u32 message;
    u64 mailbox = hhdm + MAILBOX_PHYS;
    u64 start;
    u64 ticks;
    u32 reply = 0;
    u32 drain_count = 0;

    result[R_MAILBOX_ATTEMPTS]++;

    if (!virtual_to_physical(buffer_virtual, &buffer_physical))
        return ERR_MBOX_VA_TO_PA;

    result[R_MBOX_BUF_PHYS] = buffer_physical;

    if ((buffer_physical & 0xFFFULL) != 0 || buffer_physical >= 0x40000000ULL)
        return ERR_MBOX_ADDRESS_RANGE;

    buffer[0] = 28U;
    buffer[1] = 0U;
    buffer[2] = FW_NOTIFY_XHCI_RESET;
    buffer[3] = 4U;
    buffer[4] = 0U; /* Linux sends req_resp_size = 0 for this property wrapper. */
    buffer[5] = VL805_FIRMWARE_BDF;
    buffer[6] = 0U;

    cache_clean_range((const void *)buffer, 28ULL);

    bus_address = ((u32)buffer_physical & 0x3FFFFFF0U) | MAILBOX_GPU_UNCACHED_BASE;
    message = bus_address | MAILBOX_PROPERTY_CHANNEL;
    result[R_MBOX_MESSAGE] = message;

    /* Drop stale mailbox-0 replies before starting our synchronous transaction. */
    while ((mmio_read32(mailbox + MAIL0_STATUS_OFF) & MAILBOX_EMPTY) == 0 && drain_count < 64U)
    {
        (void)mmio_read32(mailbox + MAIL0_READ_OFF);
        drain_count++;
    }

    start = timer_counter();
    ticks = timeout_ticks(1000000ULL);
    while ((mmio_read32(mailbox + MAIL1_STATUS_OFF) & MAILBOX_FULL) != 0)
    {
        if ((timer_counter() - start) >= ticks)
            return ERR_MBOX_TX_TIMEOUT;
        __asm__ volatile("yield");
    }

    mmio_write32(mailbox + MAIL1_WRITE_OFF, message);

    start = timer_counter();
    ticks = timeout_ticks(1000000ULL);
    for (;;)
    {
        if ((mmio_read32(mailbox + MAIL0_STATUS_OFF) & MAILBOX_EMPTY) == 0)
        {
            reply = mmio_read32(mailbox + MAIL0_READ_OFF);
            if ((reply & 0xFU) == MAILBOX_PROPERTY_CHANNEL &&
                (reply & 0xFFFFFFF0U) == bus_address)
                break;
        }

        if ((timer_counter() - start) >= ticks)
        {
            result[R_MBOX_REPLY] = reply;
            return ERR_MBOX_RX_TIMEOUT;
        }
        __asm__ volatile("yield");
    }

    result[R_MBOX_REPLY] = reply;
    cache_invalidate_range((const void *)buffer, 28ULL);

    result[R_MBOX_STATUS] = buffer[1];
    result[R_MBOX_TAG_STATUS] = buffer[4];

    if (buffer[1] != FW_STATUS_SUCCESS)
        return ERR_MBOX_RESPONSE;

    /* Linux waits 200..1000 us for VL805 startup. Use the conservative end. */
    delay_us(1000ULL);
    return ERR_OK;
}

static int take_legacy_ownership(u64 xhci_base, u64 *result)
{
    u32 hccparams1 = mmio_read32(xhci_base + XHCI_HCCPARAMS1_OFF);
    u32 ext_dwords = hccparams1 >> 16;
    u64 current;
    u32 hop;

    if (ext_dwords == 0)
        return ERR_OK;

    current = xhci_base + (((u64)ext_dwords) << 2);
    for (hop = 0; hop < 64U; hop++)
    {
        u32 header = mmio_read32(current);
        u32 id = header & 0xFFU;
        u32 next = (header >> 8) & 0xFFU;

        if (id == XHCI_EXTCAP_LEGACY)
        {
            u32 last = header;
            result[R_LEGSUP_BEFORE] = header;

            if ((header & XHCI_LEGACY_OS_OWNED) == 0)
            {
                mmio_write32(current, header | XHCI_LEGACY_OS_OWNED);
                header = mmio_read32(current);
            }

            if ((header & XHCI_LEGACY_BIOS_OWNED) != 0)
            {
                if (!wait_register_bits(current, XHCI_LEGACY_BIOS_OWNED, 0U, 1000000ULL, &last))
                {
                    result[R_LEGSUP_AFTER] = last;
                    return ERR_LEGACY_TIMEOUT;
                }
            }

            header = mmio_read32(current);
            result[R_LEGSUP_AFTER] = header;
            if ((header & XHCI_LEGACY_OS_OWNED) == 0)
                return ERR_LEGACY_OWNERSHIP;

            return ERR_OK;
        }

        if (next == 0)
            return ERR_OK;

        current += ((u64)next) << 2;
    }

    return ERR_LEGACY_TIMEOUT;
}

static int reset_controller_once(u64 operational_base, u64 *result)
{
    u32 command = mmio_read32(operational_base + XHCI_USBCMD_OFF);
    u32 status = mmio_read32(operational_base + XHCI_USBSTS_OFF);
    u32 last = 0;

    result[R_USBCMD_BEFORE] = command;
    result[R_USBSTS_BEFORE] = status;

    if ((status & XHCI_STS_HCE) != 0)
        return ERR_HCE_BEFORE;

    if ((status & XHCI_STS_HCH) == 0)
    {
        mmio_write32(operational_base + XHCI_USBCMD_OFF, command & ~XHCI_CMD_RUN);
        if (!wait_register_bits(operational_base + XHCI_USBSTS_OFF, XHCI_STS_HCH, XHCI_STS_HCH,
                                1000000ULL, &last))
        {
            result[R_LAST_USBSTS] = last;
            return ERR_HALT_TIMEOUT;
        }
    }

    command = mmio_read32(operational_base + XHCI_USBCMD_OFF);
    mmio_write32(operational_base + XHCI_USBCMD_OFF, (command & ~XHCI_CMD_RUN) | XHCI_CMD_HCRST);

    if (!wait_register_bits(operational_base + XHCI_USBCMD_OFF, XHCI_CMD_HCRST, 0U,
                            10000000ULL, &last))
    {
        result[R_LAST_USBCMD] = last;
        result[R_LAST_USBSTS] = mmio_read32(operational_base + XHCI_USBSTS_OFF);
        return ERR_HCRST_TIMEOUT;
    }

    if (!wait_register_bits(operational_base + XHCI_USBSTS_OFF, XHCI_STS_CNR, 0U,
                            10000000ULL, &last))
    {
        result[R_LAST_USBSTS] = last;
        return ERR_CNR_TIMEOUT;
    }

    result[R_USBCMD_AFTER] = mmio_read32(operational_base + XHCI_USBCMD_OFF);
    result[R_USBSTS_AFTER] = mmio_read32(operational_base + XHCI_USBSTS_OFF);
    result[R_PAGESIZE] = mmio_read32(operational_base + XHCI_PAGESIZE_OFF);
    result[R_CONFIG] = mmio_read32(operational_base + XHCI_CONFIG_OFF);

    if ((result[R_USBSTS_AFTER] & XHCI_STS_HCE) != 0)
        return ERR_HCE_AFTER;
    if ((result[R_USBSTS_AFTER] & XHCI_STS_HCH) == 0)
        return ERR_NOT_HALTED;
    if ((result[R_PAGESIZE] & 1ULL) == 0)
        return ERR_PAGE_SIZE;

    return ERR_OK;
}

static void clear_result(u64 *result)
{
    u32 i;
    for (i = 0; i < RESULT_WORDS; i++)
        result[i] = 0;
}

/*
 * Native Raspberry Pi 4 VL805 Stage 1.
 * Called directly from Cosmos NativeAOT via DirectPInvoke.
 * Returns 0 on success or a negative ERR_* code on a bounded failure.
 */
int zq_rpi4_xhci_stage1_native(u64 hhdm, u64 *result)
{
    u64 xhci_base = 0;
    u64 operational_base = 0;
    int rc;

    if (hhdm == 0 || result == (u64 *)0)
        return ERR_BAD_ARGUMENT;

    clear_result(result);
    result[R_MAGIC] = 0x5A51555342314331ULL; /* "ZQUSB1C1" */

    result[R_STAGE] = 1;
    rc = locate_xhci(hhdm, &xhci_base, &operational_base, result);
    if (rc != ERR_OK)
        goto failed;

    result[R_STAGE] = 2;
    rc = firmware_notify_xhci_reset(hhdm, result);
    if (rc != ERR_OK)
        goto failed;

    result[R_STAGE] = 3;
    rc = locate_xhci(hhdm, &xhci_base, &operational_base, result);
    if (rc != ERR_OK)
        goto failed;

    result[R_STAGE] = 4;
    rc = take_legacy_ownership(xhci_base, result);
    if (rc != ERR_OK)
        goto failed;

    result[R_STAGE] = 5;
    rc = reset_controller_once(operational_base, result);
    if (rc == ERR_HCRST_TIMEOUT || rc == ERR_CNR_TIMEOUT)
    {
        /* One bounded recovery attempt: reload/notify VL805 firmware, rediscover BAR,
           re-take ownership, then retry the xHCI reset from a clean platform state. */
        result[R_RETRY] = 1;
        result[R_STAGE] = 6;
        rc = firmware_notify_xhci_reset(hhdm, result);
        if (rc != ERR_OK)
            goto failed;

        delay_us(9000ULL); /* total 10 ms startup window on the recovery path */

        result[R_STAGE] = 7;
        rc = locate_xhci(hhdm, &xhci_base, &operational_base, result);
        if (rc != ERR_OK)
            goto failed;

        rc = take_legacy_ownership(xhci_base, result);
        if (rc != ERR_OK)
            goto failed;

        result[R_STAGE] = 8;
        rc = reset_controller_once(operational_base, result);
    }

    if (rc != ERR_OK)
        goto failed;

    result[R_STAGE] = 9;
    result[R_ERROR] = 0;
    return ERR_OK;

failed:
    result[R_ERROR] = (u64)(s32)rc;
    return rc;
}
