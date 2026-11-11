# Standalone x86_64 UEFI BootGuard build (GNU-EFI).
# Does NOT install anything to EFI System Partitions.
CC ?= gcc
LD ?= ld
OBJCOPY ?= objcopy

EFI_LIBDIR ?= $(firstword $(foreach p,/usr/lib/x86_64-linux-gnu /usr/lib64 /usr/lib,$(if $(wildcard $(p)/crt0-efi-x86_64.o),$(p))))
ifeq ($(strip $(EFI_LIBDIR)),)
$(error GNU-EFI missing: install gnu-efi and check EFI_LIBDIR)
endif

BUILD_DIR := build
CFLAGS := -std=gnu11 -O2 -Wall -Wextra -fpic -ffreestanding -fno-stack-protector -fno-stack-check -fshort-wchar -mno-red-zone -DEFI_FUNCTION_WRAPPER -I/usr/include/efi -I/usr/include/efi/x86_64

.PHONY: all clean
all: $(BUILD_DIR)/bootguard.efi

$(BUILD_DIR):
	mkdir -p $(BUILD_DIR)

$(BUILD_DIR)/bootguard.o: bootguard_efi.c bootguard_policy.h bootguard_elf.h | $(BUILD_DIR)
	$(CC) $(CFLAGS) -c $< -o $@

$(BUILD_DIR)/bootguard.so: $(BUILD_DIR)/bootguard.o
	$(LD) -nostdlib -znocombreloc -T $(EFI_LIBDIR)/elf_x86_64_efi.lds -shared -Bsymbolic $(EFI_LIBDIR)/crt0-efi-x86_64.o $< -L$(EFI_LIBDIR) -lgnuefi -lefi -o $@

$(BUILD_DIR)/bootguard.efi: $(BUILD_DIR)/bootguard.so
	$(OBJCOPY) -j .text -j .sdata -j .data -j .rodata -j .dynamic -j .dynsym -j .rel -j .rela -j .rel.* -j .rela.* -j .reloc --target=efi-app-x86_64 --subsystem=10 $< $@

# Test-only EFI program for a disposable QEMU/OVMF image.
$(BUILD_DIR)/limine_stub.o: ../../tests/bootguard_limine_stub.c | $(BUILD_DIR)
	$(CC) $(CFLAGS) -c $< -o $@

$(BUILD_DIR)/limine_stub.so: $(BUILD_DIR)/limine_stub.o
	$(LD) -nostdlib -znocombreloc -T $(EFI_LIBDIR)/elf_x86_64_efi.lds -shared -Bsymbolic $(EFI_LIBDIR)/crt0-efi-x86_64.o $< -L$(EFI_LIBDIR) -lgnuefi -lefi -o $@

$(BUILD_DIR)/limine_stub.efi: $(BUILD_DIR)/limine_stub.so
	$(OBJCOPY) -j .text -j .sdata -j .data -j .rodata -j .dynamic -j .dynsym -j .rel -j .rela -j .rel.* -j .rela.* -j .reloc --target=efi-app-x86_64 --subsystem=10 $< $@

clean:
	rm -rf $(BUILD_DIR)
