#include "boot_policy.h"

static int normal_slot(ZqSlot slot)
{
    return slot == ZQ_SLOT_A || slot == ZQ_SLOT_B;
}

ZqDecision zq_bootguard_prepare(ZqBootState *state, int pending_image_valid,
                                int fallback_image_valid, ZqSlot *selected)
{
    if (!state || !selected)
        return ZQ_BOOT_INVALID;
    *selected = ZQ_SLOT_RECOVERY;

    if (!normal_slot(state->active) ||
        !normal_slot(state->fallback) ||
        state->active == state->fallback ||
        state->boot_attempts > ZQ_MAX_PENDING_ATTEMPTS ||
        state->fallback_requested > 1u ||
        (state->pending != ZQ_SLOT_NONE &&
         (state->pending != state->active ||
          !normal_slot(state->pending))))
        return ZQ_BOOT_INVALID;

    if (state->pending == ZQ_SLOT_NONE)
    {
        if (state->boot_attempts != 0u || state->fallback_requested != 0u)
            return ZQ_BOOT_INVALID;
        *selected = state->active;
        return ZQ_BOOT_ACTIVE;
    }

    /* On an image parse failure, never execute the candidate. */
    if (!pending_image_valid ||
        state->boot_attempts >= ZQ_MAX_PENDING_ATTEMPTS ||
        state->fallback_requested)
    {
        state->fallback_requested = 1u;
        if (!fallback_image_valid)
        {
            *selected = ZQ_SLOT_RECOVERY;
            return ZQ_BOOT_RECOVERY;
        }
        *selected = state->fallback;
        return ZQ_BOOT_FALLBACK;
    }

    /* Must be persisted durably before invoking Limine/ELF. */
    state->boot_attempts++;
    *selected = state->pending;
    return ZQ_BOOT_PENDING;
}

int zq_bootguard_confirm(ZqBootState *state, ZqSlot running_slot)
{
    if (!state || !normal_slot(running_slot) ||
        state->pending != running_slot ||
        state->active != running_slot ||
        state->fallback == running_slot ||
        state->fallback_requested)
        return 0;
    if (state->boot_attempts < 1u ||
        state->boot_attempts > ZQ_MAX_PENDING_ATTEMPTS)
        return 0;

    state->pending = ZQ_SLOT_NONE;
    state->boot_attempts = 0u;
    state->fallback_requested = 0u;
    return 1;
}
