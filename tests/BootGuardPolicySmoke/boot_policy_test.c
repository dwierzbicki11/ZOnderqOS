#include <stdio.h>
#include <string.h>
#include "boot_policy.h"

static int checks = 0;
#define CHECK(expr) do { checks++; if (!(expr)) { \
    fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #expr); return 1; \
} } while (0)

static ZqBootState pending_state(void)
{
    ZqBootState s;
    s.active = ZQ_SLOT_B;
    s.fallback = ZQ_SLOT_A;
    s.pending = ZQ_SLOT_B;
    s.boot_attempts = 0;
    s.fallback_requested = 0;
    return s;
}

static int run(void)
{
    ZqSlot selected;
    ZqBootState s = pending_state();

    CHECK(zq_bootguard_prepare(&s, 1, 1, &selected) == ZQ_BOOT_PENDING);
    CHECK(selected == ZQ_SLOT_B && s.boot_attempts == 1);
    CHECK(zq_bootguard_prepare(&s, 1, 1, &selected) == ZQ_BOOT_PENDING);
    CHECK(selected == ZQ_SLOT_B && s.boot_attempts == 2);
    CHECK(zq_bootguard_prepare(&s, 1, 1, &selected) == ZQ_BOOT_PENDING);
    CHECK(selected == ZQ_SLOT_B && s.boot_attempts == 3);

    /* Fourth invocation: previous three attempts never confirmed health. */
    CHECK(zq_bootguard_prepare(&s, 1, 1, &selected) == ZQ_BOOT_FALLBACK);
    CHECK(selected == ZQ_SLOT_A && s.fallback_requested == 1);
    CHECK(!zq_bootguard_confirm(&s, ZQ_SLOT_B));

    /* Invalid ELF must bypass candidate regardless of attempt count. */
    s = pending_state();
    CHECK(zq_bootguard_prepare(&s, 0, 1, &selected) == ZQ_BOOT_FALLBACK);
    CHECK(selected == ZQ_SLOT_A && s.boot_attempts == 0);

    /* Missing fallback must never select a bad or unconfirmed kernel. */
    s = pending_state();
    CHECK(zq_bootguard_prepare(&s, 0, 0, &selected) == ZQ_BOOT_RECOVERY);
    CHECK(selected == ZQ_SLOT_RECOVERY);

    /* Successful third attempt is still eligible for confirmation. */
    s = pending_state();
    CHECK(zq_bootguard_prepare(&s, 1, 1, &selected) == ZQ_BOOT_PENDING);
    CHECK(zq_bootguard_prepare(&s, 1, 1, &selected) == ZQ_BOOT_PENDING);
    CHECK(zq_bootguard_prepare(&s, 1, 1, &selected) == ZQ_BOOT_PENDING);
    CHECK(!zq_bootguard_confirm(&s, ZQ_SLOT_A));
    CHECK(zq_bootguard_confirm(&s, ZQ_SLOT_B));
    CHECK(s.pending == ZQ_SLOT_NONE && s.boot_attempts == 0);
    CHECK(zq_bootguard_prepare(&s, 1, 1, &selected) == ZQ_BOOT_ACTIVE);
    CHECK(selected == ZQ_SLOT_B);

    /* Corrupt or contradictory persistent state fails closed. */
    s = pending_state();
    s.fallback = ZQ_SLOT_B;
    CHECK(zq_bootguard_prepare(&s, 1, 1, &selected) == ZQ_BOOT_INVALID);
    s = pending_state();
    s.pending = ZQ_SLOT_A;
    CHECK(zq_bootguard_prepare(&s, 1, 1, &selected) == ZQ_BOOT_INVALID);
    s = pending_state();
    s.boot_attempts = 4;
    CHECK(zq_bootguard_prepare(&s, 1, 1, &selected) == ZQ_BOOT_INVALID);
    s = pending_state();
    s.pending = ZQ_SLOT_NONE;
    s.boot_attempts = 1;
    CHECK(zq_bootguard_prepare(&s, 1, 1, &selected) == ZQ_BOOT_INVALID);
    s = pending_state();
    CHECK(zq_bootguard_prepare(0, 1, 1, &selected) == ZQ_BOOT_INVALID);
    CHECK(zq_bootguard_prepare(&s, 1, 1, 0) == ZQ_BOOT_INVALID);

    /* Never count an attempt as a confirmed boot. */
    s = pending_state();
    CHECK(!zq_bootguard_confirm(&s, ZQ_SLOT_B));
    CHECK(s.pending == ZQ_SLOT_B);

    fprintf(stdout, "PASS: %d BootGuard policy assertions\n", checks);
    return 0;
}

int main(void) { return run(); }
