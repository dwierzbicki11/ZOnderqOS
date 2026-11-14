#ifndef ZONDERQ_BOOT_POLICY_H
#define ZONDERQ_BOOT_POLICY_H

/* Portable EFI BootGuard decision core. No disk writes or UEFI calls here.
 * Firmware adapter MUST persist the returned state before chainloading Limine.
 * Do not use this library alone as a bootloader or recovery guarantee.
 */

#ifdef __cplusplus
extern "C" {
#endif

typedef enum ZqSlot {
    ZQ_SLOT_NONE = 0,
    ZQ_SLOT_A = 1,
    ZQ_SLOT_B = 2,
    ZQ_SLOT_RECOVERY = 3
} ZqSlot;

typedef enum ZqDecision {
    ZQ_BOOT_INVALID = 0,
    ZQ_BOOT_PENDING = 1,
    ZQ_BOOT_ACTIVE = 2,
    ZQ_BOOT_FALLBACK = 3,
    ZQ_BOOT_RECOVERY = 4
} ZqDecision;

typedef struct ZqBootState {
    ZqSlot active;
    ZqSlot fallback;
    ZqSlot pending;
    unsigned int boot_attempts;
    unsigned int fallback_requested;
} ZqBootState;

#define ZQ_MAX_PENDING_ATTEMPTS 3u

/*
 * Returns selected slot (or recovery). When pending is selected,
 * boot_attempts is INCREMENTED before the handoff, and adapter MUST durably
 * persist this modified state. The 3rd attempt still boots pending;
 * on next invocation fallback is selected.
 *
 * Pending boot health is confirmed only by kernel after a real usable
 * session. Boot selection does NOT mark success.
 */
ZqDecision zq_bootguard_prepare(ZqBootState *state, int pending_image_valid,
                                int fallback_image_valid, ZqSlot *selected);

/* Called only after the running kernel confirms intended interactive mode. */
int zq_bootguard_confirm(ZqBootState *state, ZqSlot running_slot);

#ifdef __cplusplus
}
#endif
#endif
