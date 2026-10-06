# Building on Linux

**English** · [Русский](README.build.linux.ru.md) · [Overview](README.md) · [Windows build](README.build.windows.md)

Tested on Debian 13 (x86-64). Other distributions work with the equivalent
packages.

## 1. Dependencies

```bash
sudo apt-get install -y build-essential git ninja-build meson pkg-config \
    python3 python3-venv libglib2.0-dev libpixman-1-dev libslirp-dev \
    libfdt-dev zlib1g-dev \
    mtd-utils wget u-boot-tools device-tree-compiler \
    bridge-utils iproute2 iptables libpcap0.8t64
```

- `mtd-utils` (`ubinize`), `wget` — building NAND images
  ([`tools/prepare-nand.sh`](tools/prepare-nand.sh), [`tools/mknand.py`](tools/mknand.py)).
- `bridge-utils`, `iproute2`, `iptables` — host networking ([`tools/host-bridge.sh`](tools/host-bridge.sh)).
- `libpcap` — only for the optional `-netdev pcap` backend (loaded at run time).

## 2. Build QEMU with the mt7981-router machine

```bash
./build.sh
```

What [`build.sh`](build.sh) does:

1. clones QEMU **v10.1.0** into `src/qemu` (shallow);
2. creates branch `mt7981` and applies [`qemu-patches/*.patch`](qemu-patches/) with `git am`;
3. configures `--target-list=aarch64-softmmu --enable-slirp --enable-fdt=system`;
4. builds with `ninja`.

Result: `src/qemu/build/qemu-system-aarch64`. Check:

```bash
src/qemu/build/qemu-system-aarch64 -M help | grep mt7981
src/qemu/build/qemu-system-aarch64 -M mt7981-router,help    # board options
```

Manual equivalent:

```bash
git clone --depth 1 --branch v10.1.0 https://gitlab.com/qemu-project/qemu.git src/qemu
cd src/qemu && git checkout -b mt7981 && git am ../../qemu-patches/*.patch
mkdir build && cd build
../configure --target-list=aarch64-softmmu --enable-slirp --enable-fdt=system --disable-docs
ninja
```

After changing sources in `src/qemu/hw/arm/mt7981/` just run `ninja` in
`src/qemu/build`. To update the patch series:
`cd src/qemu && git commit ... && git format-patch -o ../../qemu-patches v10.1.0..mt7981`.

## 3. Flash images

```bash
tools/prepare-nand.sh cudy_wr3000p-v1 25.12.5        # -> nand-wr3000p/
tools/prepare-nand.sh cudy_tr3000-v1 25.12.5         # -> nand-tr3000/
tools/prepare-nand.sh cudy_wr3000p-v1 snapshot       # snapshot instead of a release
# vendor bootloader kept (dumps of BL2 = *mtd0*.bin, FIP = *mtd4*.bin in wr3000u/)
tools/prepare-nand.sh --stock wr3000u --flash-mb 256 cudy_wr3000u-v1 25.12.5
# own OpenWrt build (the *-ubootmod-* images in a folder)
tools/prepare-nand.sh --local m3000/<build> cudy_m3000-v1 25.12.5
# boards without a bdinfo partition (FIP 0x380000, ubi 0x580000)
tools/prepare-nand.sh --no-bdinfo netis_nx31 25.12.5
# SPI-NOR board: vendor BL2/FIP dumps in wr3000/ + OpenWrt sysupgrade.bin
tools/prepare-nand.sh --stock wr3000 --nor cudy_wr3000-v1 25.12.5
```

PROFILE is the OpenWrt device profile. The output folder is `nand-NAME`
(profile without vendor prefix and `-v1`), as used by the presets' `nand-dir`.

Put the board's own `*Factory*.bin` (Wi-Fi calibration) and `*bdinfo*.bin`
(MAC address) into `factory/` before; without them Wi-Fi uses defaults and a
random MAC is generated.

## 4. Host networking (optional)

```bash
sudo tools/host-bridge.sh setup enp0s3 br0   # move the NIC into br0 (keeps IP/MAC, persistent)
sudo tools/host-bridge.sh taps br0 $USER     # wr-wan -> br0, wr-lan1..4 -> isolated br-wrlan
tools/host-bridge.sh status
sudo tools/host-bridge.sh teardown           # undo
```

In a VM (e.g. VirtualBox) set the host adapter's promiscuous mode to
"Allow All", otherwise frames for the router's MAC addresses are not
delivered. `setup` keeps a backup of `/etc/network/interfaces`.

## 5. Run

```bash
./mt7981.sh                      # preset cudy-wr3000p-v1, WAN on br0, lan1 on br-wrlan
./mt7981.sh -P list              # presets (presets/*.ini)
./mt7981.sh -P cudy-tr3000-v1 -w user -l none
./mt7981.sh -P cudy-wr3000p-v1 -o usb-port=3,ram=1024   # change the hardware
./mt7981.sh -h                   # all options
```

Console: this terminal (Ctrl-A X quits, Ctrl-A C = QEMU monitor). Logs:
`logs/console_*.log`. Fast automated checks: [`tests/quick.py`](tests/quick.py).
