#!/usr/bin/env python3
"""
Build an eMMC image for the emulator (machine option flash=emmc), e.g. for
the GL.iNet GL-MT6000 (MT7986A) with the OpenWrt U-Boot.

The image file is what the emulated eMMC holds, in this order:
  boot0   (--boot-mb, default 4 MB)  BL2 preloader ("EMMC_BOOT" header)
  boot1   (--boot-mb)                empty
  user    GPT disk, the rest of the image (total size a power of 2)

User area layout (GPT, like the GL-MT6000; partitions are found by name):
  0x0400000  u-boot-env    512 KB  (also U-Boot's raw env offset)
  0x0480000  factory         2 MB  (Wi-Fi EEPROM, MAC at 0xa)
  0x0680000  fip             4 MB  (BL31 + U-Boot)
  0x1000000  kernel         32 MB  \\ OpenWrt squashfs-factory.bin =
  0x3000000  rootfs        512 MB  / kernel padded to 32 MB + rootfs

The file is sparse: unused space takes no disk space.

Example:
  mkemmc.py -o emmc-gl-mt6000/mt7986.emmc.img --bl2 preloader.bin \\
      --fip bl31-uboot.fip --firmware squashfs-factory.bin
"""
import argparse
import os
import random
import shutil
import subprocess
import sys

MB = 1024 * 1024
PARTS = [  # name, offset, size
    ("u-boot-env", 0x0400000, 0x80000),
    ("factory", 0x0480000, 0x200000),
    ("fip", 0x0680000, 0x400000),
    ("kernel", 0x1000000, 32 * MB),
    ("rootfs", 0x3000000, 512 * MB),
]


def wifi_eeprom(chip, mac):
    """Minimal MediaTek Wi-Fi EEPROM (as tools/mknand.py --wifi-eeprom)."""
    e = bytearray(0x1000)
    e[0:2] = chip.to_bytes(2, "little")
    e[4:10] = mac
    e[0x190] = (0 << 6) | (4 << 3) | 4
    e[0x191] = (1 << 6) | (4 << 3) | 4
    e[0x192] = e[0x193] = 4 << 5
    return e


def put(f, off, data):
    f.seek(off)
    f.write(data)


def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("-o", "--output", required=True, help="image file")
    p.add_argument("--size-mb", type=int, default=1024,
                   help="whole image incl. boot partitions, a power of 2 "
                        "(default 1024)")
    p.add_argument("--boot-mb", type=int, default=4,
                   help="size of each boot partition (machine option "
                        "emmc-boot, default 4)")
    p.add_argument("--bl2", required=True, help="eMMC preloader (EMMC_BOOT)")
    p.add_argument("--fip", required=True, help="bl31-uboot.fip")
    p.add_argument("--firmware", help="OpenWrt squashfs-factory.bin "
                   "(kernel padded to 32 MB + rootfs)")
    p.add_argument("--factory", help="factory partition dump (else a minimal "
                   "Wi-Fi EEPROM with a random MAC)")
    p.add_argument("--wifi-chip", default="7986",
                   help="chip ID of the generated EEPROM (hex)")
    a = p.parse_args()

    size = a.size_mb * MB
    boot = a.boot_mb * MB
    if size & (size - 1):
        sys.exit("--size-mb must be a power of 2 (QEMU's eMMC needs that)")
    user = size - 2 * boot
    last = PARTS[-1][1] + PARTS[-1][2]
    if last + 1 * MB > user:
        sys.exit(f"image too small: the partitions need {last // MB + 1} MB")
    sgdisk = shutil.which("sgdisk") or shutil.which(
        "sgdisk", path="/usr/sbin:/sbin:/usr/local/sbin")
    if not sgdisk:
        sys.exit("sgdisk not found (apt install gdisk)")

    os.makedirs(os.path.dirname(os.path.abspath(a.output)), exist_ok=True)
    tmp = a.output + ".user"
    with open(tmp, "wb") as f:
        f.truncate(user)
    cmd = [sgdisk, "-o", "-a", "1"]
    for i, (name, off, sz) in enumerate(PARTS, 1):
        cmd += ["-n", f"{i}:{off // 512}:{(off + sz) // 512 - 1}",
                "-c", f"{i}:{name}"]
    subprocess.run(cmd + [tmp], check=True, stdout=subprocess.DEVNULL)

    part = {name: off for name, off, _ in PARTS}
    with open(tmp, "r+b") as f:
        put(f, part["fip"], open(a.fip, "rb").read())
        if a.factory:
            put(f, part["factory"], open(a.factory, "rb").read()[:0x200000])
        else:
            mac = bytes([0x02] + [random.randrange(256) for _ in range(5)])
            eep = wifi_eeprom(int(a.wifi_chip, 16), mac)
            # Ethernet MAC base (mac-base cell at 0xa on the GL-MT6000)
            eep[0xa:0x10] = bytes([0x06]) + mac[1:]
            put(f, part["factory"], bytes(eep))
            print("factory: minimal Wi-Fi EEPROM, MAC %s"
                  % ":".join("%02x" % b for b in mac))
        if a.firmware:
            fw = open(a.firmware, "rb").read()
            room = PARTS[-1][1] + PARTS[-1][2] - part["kernel"]
            if len(fw) > room:
                sys.exit("firmware larger than kernel + rootfs")
            put(f, part["kernel"], fw)

    with open(a.output, "wb") as out:
        out.truncate(size)
        bl2 = open(a.bl2, "rb").read()
        if bl2[:9] != b"EMMC_BOOT":
            print("warning: the BL2 has no EMMC_BOOT header", file=sys.stderr)
        put(out, 0, bl2)
        # copy the user area, keeping holes
        with open(tmp, "rb") as f:
            off = 0
            while off < user:
                chunk = f.read(4 * MB)
                if chunk.strip(b"\0"):
                    put(out, 2 * boot + off, chunk)
                off += len(chunk)
    os.unlink(tmp)
    print(f"wrote {a.output} ({a.size_mb} MB: boot0/boot1 {a.boot_mb} MB, "
          f"user area {user // MB} MB)")


if __name__ == "__main__":
    main()
