#include <assert.h>
#include <stdio.h>
#include "../tools/bootguard/bootguard_policy.h"

static void test_three_failures(void) {
    zq_boot_state s = {'B','A','B',0,1,0};
    char target;
    int dirty;
    for (unsigned i = 1; i <= 3; i++) {
        assert(zq_boot_decide(&s,&target,&dirty));
        assert(target == 'B');
        assert(dirty == 1);
        assert(s.attempts == i);
        assert(s.recovery_requested == 0);
    }
    // A corrupted ELF may never execute C#; BootGuard still runs on boot #4.
    assert(zq_boot_decide(&s,&target,&dirty));
    assert(target == 'A' && dirty == 1);
    assert(s.recovery_requested == 1);
    assert(zq_boot_decide(&s,&target,&dirty));
    assert(target == 'A' && dirty == 0);
}

static void test_healthy_and_reject(void) {
    char target;
    int dirty;
    zq_boot_state good = {'A','B',0,0,1,0};
    assert(zq_boot_decide(&good,&target,&dirty));
    assert(target == 'A' && !dirty);
    zq_boot_state disabled = {'B','A','B',0,0,0};
    assert(!zq_boot_decide(&disabled,&target,&dirty));
    zq_boot_state contradictory = {'A','A','B',0,1,0};
    assert(!zq_boot_decide(&contradictory,&target,&dirty));
    zq_boot_state invalid = {'B','A','B',4,1,0};
    assert(!zq_boot_decide(&invalid,&target,&dirty));
    zq_boot_state bad_flag = {'A','B',0,0,1,1};
    assert(!zq_boot_decide(&bad_flag,&target,&dirty));
    zq_boot_state pending = {'A','B','A',2,1,0};
    assert(zq_boot_decide(&pending,&target,&dirty));
    assert(target=='A' && pending.attempts == 3);
}

int main(void) {
    test_three_failures();
    test_healthy_and_reject();
    puts("BOOTGUARD POLICY PASS: third attempted boot and preboot fallback");
    return 0;
}
