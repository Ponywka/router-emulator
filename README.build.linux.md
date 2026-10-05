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

## 2. Build QEMU with the WR3000X machines

```bash
./build.sh
```

What [`build.sh`](build.sh) does:

1. clones QEMU **v10.1.0** into `src/qemu` (shallow);
2. creates branch `wr3000x` and applies [`qemu-patches/*.patch`](qemu-patches/) with `git am`;
3. configures `--target-list=aarch64-softmmu --enable-slirp --enable-fdt=system`;
4. builds with `ninja`.

Result: `src/qemu/build/qemu-system-aarch64`. Check:

```bash
src/qemu/build/qemu-system-aarch64 -M help | grep cudy
```

Manual equivalent:

```bash
git clone --depth 1 --branch v10.1.0 https://gitlab.com/qemu-project/qemu.git src/qemu
cd src/qemu && git checkout -b wr3000x && git am ../../qemu-patches/*.patch
mkdir build && cd build
../configure --target-list=aarch64-softmmu --enable-slirp --enable-fdt=system --disable-docs
ninja
```

After changing sources in `src/qemu/hw/arm/mt7981/` just run `ninja` in
`src/qemu/build`. To update the patch series:
`cd src/qemu && git commit ... && git format-patch -o ../../qemu-patches v10.1.0..wr3000x`.

## 3. Flash images

```bash
tools/prepare-nand.sh wr3000p 25.12.5        # -> nand-wr3000p/
tools/prepare-nand.sh wr3000h 25.12.5        # -> nand-wr3000h/
tools/prepare-nand.sh wr3000s 25.12.5
tools/prepare-nand.sh wbr3000uax 25.12.5
tools/prepare-nand.sh wr3000u 25.12.5        # needs wr3000u/*mtd0*.bin, *mtd4*.bin (+ sysupgrade.bin)
tools/prepare-nand.sh wr3000p snapshot       # snapshot instead of a release
```

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
./wr3000x.sh                     # WR3000P, WAN on br0, lan1 on br-wrlan
./wr3000x.sh -b cudy-wr3000u -n nand-wr3000u -w user -l none
./wr3000x.sh -h                  # all options
```

Console: this terminal (Ctrl-A X quits, Ctrl-A C = QEMU monitor). Logs:
`logs/console_*.log`. Fast automated checks: [`tests/quick.py`](tests/quick.py).
