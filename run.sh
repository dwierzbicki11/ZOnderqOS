#!/bin/bash
cosmos build

qemu-system-x86_64 \
-L "/home/zonderq/.cosmos/tools/share/qemu" \
-M q35 \
-cpu max \
-m 512M \
-drive file="/mnt/CosmosKernel/ZonderqOS/output-x64/ZonderqOS.iso",if=none,id=cosmoscd,format=raw,readonly=on \
-device ide-cd,drive=cosmoscd,bootindex=0 \
-boot d \
-no-reboot \
-no-shutdown \
-display gtk,fullscreen=on,zoom-to-fit=on \
-serial stdio \
-device ich9-ahci,id=ahci0 \
-drive file="zonder_disk.img",if=none,id=ahcidisk0,format=raw \
-device ide-hd,drive=ahcidisk0,bus=ahci0.0 \
-drive file="disk_sata_1G.img",if=none,id=ahcidisk1,format=raw \
-device ide-hd,drive=ahcidisk1,bus=ahci0.1 \
-drive file="disk_nvme_2G.img",if=none,id=nvmedisk0,format=raw \
-device nvme,drive=nvmedisk0,serial=nvme-1 \
-netdev user,id=net0 \
-device e1000e,netdev=net0 \
-netdev user,id=net1 \
-device virtio-net-pci,netdev=net1
