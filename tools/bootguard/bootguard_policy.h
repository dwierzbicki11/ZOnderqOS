#ifndef ZONDERQ_BOOTGUARD_POLICY_H
#define ZONDERQ_BOOTGUARD_POLICY_H

#include "../efi-bootguard/boot_policy.h"

/*
 * Portable, testable pre-kernel A/B selection policy. No file operations.
 * EFI adapter must durably write changed state BEFORE loading Limine.
 * Invalid state never selects a pending/unverified kernel.
 */
typedef struct {
    char active;
    char fallback;
    char pending;
    unsigned int attempts;
    int enabled;
    int recovery_requested;
} zq_boot_state;

/* 1 = a safe choice; 0 = refuse and enter recovery/manual boot menu */
static inline int zq_boot_decide_with_images(zq_boot_state *s,
                                             int pending_image_valid,
                                             int fallback_image_valid,
                                             char *selected,
                                             int *dirty) {
    if (!s || !selected || !dirty) return 0;
    *selected = 0;
    *dirty = 0;
    if ((s->enabled != 0 && s->enabled != 1) ||
        (s->recovery_requested != 0 && s->recovery_requested != 1) ||
        !s->enabled ||
        (s->active != 'A' && s->active != 'B') ||
        (s->fallback != 'A' && s->fallback != 'B') ||
        s->active == s->fallback ||
        s->attempts > ZQ_MAX_PENDING_ATTEMPTS)
        return 0;
    if (s->pending == 0) {
        if (s->recovery_requested || s->attempts != 0) return 0;
        *selected = s->active;
        return 1;
    }
    if ((s->pending != 'A' && s->pending != 'B') ||
        s->pending != s->active)
        return 0;
    if (!pending_image_valid || s->recovery_requested ||
        s->attempts >= ZQ_MAX_PENDING_ATTEMPTS) {
        if (!fallback_image_valid)
            return 0;
        *selected = s->fallback;
        if (!s->recovery_requested) {
            s->recovery_requested = 1;
            *dirty = 1;
        }
        return 1;
    }
    s->attempts++;
    *dirty = 1;
    *selected = s->pending;
    return 1;
}

static inline int zq_boot_decide(zq_boot_state *s, char *selected, int *dirty) {
    return zq_boot_decide_with_images(s, 1, 1, selected, dirty);
}

#endif
