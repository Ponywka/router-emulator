#!/usr/bin/env python3
"""
Build / edit a raw SPI-NAND image (with OOB) for the Cudy WR3000X emulator.

NAND geometry: W25N01GV-like, 2048 byte pages + 64 byte OOB, 64 pages/block,
1024 blocks (128 MiB).  The output file holds every page as 2112 raw bytes,
exactly like a dump made with a NAND programmer.

Layout (OpenWrt "ubootmod" layout, see mt7981b-cudy-wr3000p-v1-ubootmod.dts):
  0x000000  BL2          (preloader.bin, contains SPINAND! + GFH header)
  0x100000  u-boot-env   (unused by OpenWrt U-Boot, env lives in UBI)
  0x180000  Factory      (Wi-Fi EEPROM)
  0x380000  bdinfo       (MAC address at 0xde00)
  0x3c0000  FIP          (BL31 + U-Boot)
  0x5c0000  ubi          (UBI: fit, recovery, ubootenv, rootfs_data ...)

The emulator normally uses a directory of per-partition dumps (data only,
no OOB); every file whose name contains "mtdN" is used, ordered by N:
  nand/cudy_wr3000x.mtd0.BL2.bin ... nand/cudy_wr3000x.mtd5.ubi.bin
They are concatenated in mtd order to form the full flash; the emulator
writes changes back to these files.

Examples:
  mknand.py create -o nand/ --bl2 preloader.bin --fip bl31-uboot.fip \
      --factory cudy_wr3000x-mtd2.Factory.bin --bdinfo bdinfo.bin \
      --fit sysupgrade.itb --recovery initramfs-recovery.itb
  mknand.py write  -i nand/ --part fip --file new.fip
  mknand.py split  -i full-dump.bin -o nand/          (full dump -> dir)
  mknand.py join   -i nand/ -o nand-raw.bin           (dir -> raw w/ OOB)
  mknand.py create -o nand.bin --bl2 preloader.bin --fip bl31-uboot.fip \
      --fit sysupgrade.itb --recovery initramfs-recovery.itb
  mknand.py write  -i nand.bin --offset 0x3c0000 --file new.fip
  mknand.py read   -i nand.bin --offset 0 --size 0x100000 -o bl2-dump.bin
  mknand.py strip  -i nand.bin -o nand-nooob.bin      (remove OOB)
  mknand.py addoob -i dump-nooob.bin -o nand.bin      (add empty OOB)
"""
import argparse
import os
import random
import shutil
import subprocess
import sys
import tempfile

PAGE = 2048
OOB = 64
PPB = 64
BLOCKS = 1024
BLOCK = PAGE * PPB
RAW_PAGE = PAGE + OOB
TOTAL = PAGE * PPB * BLOCKS
RAW_TOTAL = RAW_PAGE * PPB * BLOCKS

PARTS = {
    "bl2": (0x000000, 0x100000),
    "u-boot-env": (0x100000, 0x080000),
    "factory": (0x180000, 0x200000),
    "bdinfo": (0x380000, 0x040000),
    "fip": (0x3c0000, 0x200000),
    "ubi": (0x5c0000, TOTAL - 0x5c0000),
}
# file names like OpenWrt backups: cudy_wr3000x.mtdN.<label>.bin
PART_FILES = ["BL2", "u-boot-env", "Factory", "bdinfo", "FIP", "ubi"]
PREFIX = "cudy_wr3000x"


def set_flash_mb(mb):
    """Switch geometry to a 128 MiB (1024 blocks) or 256 MiB (2048) flash."""
    global BLOCKS, TOTAL, RAW_TOTAL
    BLOCKS = mb * 1024 * 1024 // BLOCK
    TOTAL = PAGE * PPB * BLOCKS
    RAW_TOTAL = RAW_PAGE * PPB * BLOCKS
    PARTS["ubi"] = (0x5c0000, TOTAL - 0x5c0000)


def parse_int(s):
    return int(s, 0)


def logical_to_raw(data):
    """Convert a data-only image (len multiple of PAGE) to raw with OOB."""
    out = bytearray()
    for off in range(0, len(data), PAGE):
        out += data[off:off + PAGE].ljust(PAGE, b"\xff")
        out += b"\xff" * OOB
    return out


def raw_to_logical(raw):
    out = bytearray()
    for off in range(0, len(raw), RAW_PAGE):
        out += raw[off:off + PAGE]
    return out


def dir_files(path):
    """Files whose name contains "mtdN", ordered by partition number N."""
    import re
    files = {}
    for name in os.listdir(path):
        m = re.search(r"mtd([0-9]+)", name)
        if m:
            n = int(m.group(1))
            if n in files:
                sys.exit(f"{path}: two files for mtd{n}: {files[n]} and {name}")
            files[n] = name
    return [os.path.join(path, files[n]) for n in sorted(files)]


class Nand:
    def __init__(self, path=None):
        if path and os.path.isdir(path):
            data = b"".join(open(f, "rb").read() for f in dir_files(path))
            if len(data) > TOTAL:
                sys.exit(f"{path}: partition files are larger than the flash")
            self.raw = bytearray(logical_to_raw(data.ljust(TOTAL, b"\xff")))
            return
        if path:
            raw = open(path, "rb").read()
            if len(raw) == TOTAL:
                raw = logical_to_raw(raw)
            if len(raw) != RAW_TOTAL:
                sys.exit(f"{path}: size {len(raw)} is neither {RAW_TOTAL} "
                         f"(with OOB) nor {TOTAL} (data only)")
            self.raw = bytearray(raw)
        else:
            self.raw = bytearray(b"\xff" * RAW_TOTAL)

    def write(self, offset, data, erase=True):
        if offset % PAGE:
            sys.exit("offset must be page aligned")
        if erase:
            # erase whole blocks covered by the write
            first = offset // BLOCK
            last = (offset + max(len(data), 1) - 1) // BLOCK
            for b in range(first, last + 1):
                s = b * PPB * RAW_PAGE
                self.raw[s:s + PPB * RAW_PAGE] = b"\xff" * (PPB * RAW_PAGE)
        for i in range(0, len(data), PAGE):
            page = (offset + i) // PAGE
            chunk = data[i:i + PAGE].ljust(PAGE, b"\xff")
            s = page * RAW_PAGE
            self.raw[s:s + PAGE] = chunk

    def read(self, offset, size):
        out = bytearray()
        while size > 0:
            page, col = divmod(offset, PAGE)
            n = min(size, PAGE - col)
            s = page * RAW_PAGE + col
            out += self.raw[s:s + n]
            offset += n
            size -= n
        return bytes(out)

    def save(self, path):
        if path.endswith("/") or os.path.isdir(path):
            self.save_dir(path)
            return
        with open(path, "wb") as f:
            f.write(self.raw)

    def save_dir(self, path):
        """Write one data-only file per partition (mtd0..mtd5)."""
        os.makedirs(path, exist_ok=True)
        for f in dir_files(path):
            os.unlink(f)
        for i, (key, label) in enumerate(zip(PARTS, PART_FILES)):
            off, size = PARTS[key]
            fn = os.path.join(path, f"{PREFIX}.mtd{i}.{label}.bin")
            with open(fn, "wb") as f:
                f.write(self.read(off, size))
            print(f"  {fn}  (0x{off:07x}, 0x{size:x} bytes)")


def build_ubi(args, tmp):
    vols = []
    if getattr(args, "sysupgrade", None):
        # stock OpenWrt NAND layout: UBI volumes kernel + rootfs (+ data)
        import tarfile
        with tarfile.open(args.sysupgrade) as t:
            for m in t.getmembers():
                base = os.path.basename(m.name)
                if base in ("kernel", "root") and m.isfile():
                    out = os.path.join(tmp, base)
                    with open(out, "wb") as f:
                        f.write(t.extractfile(m).read())
        for name, fn in (("kernel", "kernel"), ("rootfs", "root")):
            path = os.path.join(tmp, fn)
            if not os.path.exists(path):
                sys.exit(f"{args.sysupgrade}: no '{fn}' in sysupgrade tar")
            vols.append((name, path, "dynamic", None))
    if args.fit:
        vols.append(("fit", args.fit, "dynamic", None))
    if args.recovery:
        vols.append(("recovery", args.recovery, "dynamic", None))
    if not vols:
        return None
    ini = os.path.join(tmp, "ubinize.cfg")
    with open(ini, "w") as f:
        for i, (name, image, vtype, size) in enumerate(vols):
            f.write(f"[{name}]\nmode=ubi\nvol_id={i}\nvol_type={vtype}\n"
                    f"vol_name={name}\nimage={os.path.abspath(image)}\n\n")
        n = len(vols)
        if getattr(args, "sysupgrade", None):
            f.write(f"[rootfs_data]\nmode=ubi\nvol_id={n}\nvol_type=dynamic\n"
                    f"vol_name=rootfs_data\nvol_size=1MiB\nvol_flags=autoresize\n\n")
        else:
            for name in ("ubootenv", "ubootenv2"):
                f.write(f"[{name}]\nmode=ubi\nvol_id={n}\nvol_type=dynamic\n"
                        f"vol_name={name}\nvol_size=0x100000\n\n")
                n += 1
    out = os.path.join(tmp, "ubi.img")
    ubinize = shutil.which("ubinize") or shutil.which(
        "ubinize", path="/usr/sbin:/sbin:/usr/local/sbin")
    if not ubinize:
        sys.exit("ubinize not found (apt install mtd-utils)")
    subprocess.run([ubinize, "-o", out, "-p", str(BLOCK), "-m", str(PAGE),
                    "-s", str(PAGE), ini], check=True)
    return open(out, "rb").read()


def cmd_create(args):
    nand = Nand(args.input) if args.input else Nand()
    if args.bl2:
        nand.write(PARTS["bl2"][0], open(args.bl2, "rb").read())
    if args.fip:
        data = open(args.fip, "rb").read()
        if len(data) > PARTS["fip"][1]:
            sys.exit("FIP too large")
        nand.write(PARTS["fip"][0], data)
    if args.factory:
        nand.write(PARTS["factory"][0], open(args.factory, "rb").read())
    if args.bdinfo:
        nand.write(PARTS["bdinfo"][0], open(args.bdinfo, "rb").read())
    mac = args.mac
    if mac is None and not args.input and not args.bdinfo:
        mac = "80:af:ca:%02x:%02x:%02x" % tuple(random.randrange(256)
                                               for _ in range(3))
    if mac:
        b = bytes(int(x, 16) for x in mac.split(":"))
        if len(b) != 6:
            sys.exit("bad MAC")
        bd = bytearray(b"\xff" * PARTS["bdinfo"][1])
        bd[0xde00:0xde06] = b
        nand.write(PARTS["bdinfo"][0], bytes(bd))
        print(f"base MAC: {mac}")
    with tempfile.TemporaryDirectory() as tmp:
        ubi = build_ubi(args, tmp)
    if ubi is not None:
        if len(ubi) > PARTS["ubi"][1]:
            sys.exit("UBI image too large")
        # erase the full ubi partition first: fresh UBI attach
        off, size = PARTS["ubi"]
        nand.write(off, b"\xff" * size)
        nand.write(off, ubi, erase=False)
    nand.save(args.output)
    print(f"wrote {args.output}")


def resolve_offset(args):
    if args.part:
        return PARTS[args.part][0]
    return args.offset


def cmd_write(args):
    nand = Nand(args.input)
    nand.write(resolve_offset(args), open(args.file, "rb").read())
    out = args.output or args.input
    nand.save(out)


def cmd_read(args):
    nand = Nand(args.input)
    off = resolve_offset(args)
    size = args.size or (PARTS[args.part][1] if args.part else TOTAL - off)
    open(args.output, "wb").write(nand.read(off, size))


def cmd_strip(args):
    open(args.output, "wb").write(raw_to_logical(open(args.input, "rb").read()))


def cmd_addoob(args):
    data = open(args.input, "rb").read()
    if len(data) != TOTAL:
        sys.exit(f"input must be {TOTAL} bytes")
    open(args.output, "wb").write(logical_to_raw(data))


def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--flash-mb", type=int, choices=(128, 256), default=128,
                   help="flash size (WR3000U: 256)")
    sub = p.add_subparsers(dest="cmd", required=True)

    c = sub.add_parser("create", help="create a NAND image")
    c.add_argument("-o", "--output", required=True,
                   help="raw image file (with OOB) or directory/ for "
                        "per-partition files")
    c.add_argument("-i", "--input", help="start from an existing image/dir")
    c.add_argument("--bl2")
    c.add_argument("--fip")
    c.add_argument("--factory")
    c.add_argument("--bdinfo", help="bdinfo partition dump (MAC at 0xde00)")
    c.add_argument("--mac", help="base MAC (bdinfo 0xde00)")
    c.add_argument("--fit", help="OpenWrt sysupgrade .itb -> UBI volume 'fit'")
    c.add_argument("--recovery", help="initramfs recovery .itb -> UBI 'recovery'")
    c.add_argument("--sysupgrade", help="stock-layout OpenWrt sysupgrade.bin (tar) "
                   "-> UBI volumes kernel, rootfs, rootfs_data")
    c.set_defaults(func=cmd_create)

    for name, fn, h in (("write", cmd_write, "write a file at an offset"),
                        ("read", cmd_read, "read data from the image")):
        w = sub.add_parser(name, help=h)
        w.add_argument("-i", "--input", required=True)
        w.add_argument("-o", "--output", required=(name == "read"))
        g = w.add_mutually_exclusive_group(required=True)
        g.add_argument("--offset", type=parse_int)
        g.add_argument("--part", choices=PARTS.keys())
        if name == "write":
            w.add_argument("--file", required=True)
        else:
            w.add_argument("--size", type=parse_int)
        w.set_defaults(func=fn)

    s = sub.add_parser("strip", help="raw image -> data-only image")
    s.add_argument("-i", "--input", required=True)
    s.add_argument("-o", "--output", required=True)
    s.set_defaults(func=cmd_strip)
    sp = sub.add_parser("split", help="image or dir -> per-partition dir")
    sp.add_argument("-i", "--input", required=True)
    sp.add_argument("-o", "--output", required=True)
    sp.set_defaults(func=lambda a: Nand(a.input).save_dir(a.output))
    j = sub.add_parser("join", help="per-partition dir -> raw image with OOB")
    j.add_argument("-i", "--input", required=True)
    j.add_argument("-o", "--output", required=True)
    j.set_defaults(func=lambda a: open(a.output, "wb").write(Nand(a.input).raw))
    a = sub.add_parser("addoob", help="data-only dump -> raw image")
    a.add_argument("-i", "--input", required=True)
    a.add_argument("-o", "--output", required=True)
    a.set_defaults(func=cmd_addoob)

    args = p.parse_args()
    set_flash_mb(args.flash_mb)
    args.func(args)


if __name__ == "__main__":
    main()
