# WR3000X — Cudy WR3000X family router emulator (MediaTek MT7981B)

**English** · [Русский](README.ru.md) · Build: [Linux](README.build.linux.md) · [Windows](README.build.windows.md)

A QEMU machine that emulates the **Cudy WR3000P, WR3000H, WR3000S, WR3000U
and WBR3000UAX** routers at the hardware level. Firmware runs **unmodified**,
through the same boot chain as on the real device:

```
BootROM (emulated) → BL2 (MediaTek preloader, DDR training) → BL31 (TF-A)
→ U-Boot → OpenWrt (kernel + rootfs from UBI on SPI-NAND)
```

Both OpenWrt's own bootloader ("ubootmod" layout) and the stock Cudy
bootloader (BL2/FIP dumped from a real WR3000U, NMBM) boot. Everything the
emulator needed was adapted on the emulator side; no firmware is patched.

## Quick start

Linux: [README.build.linux.md](README.build.linux.md), then

```bash
tools/prepare-nand.sh wr3000p 25.12.5   # official images -> nand-wr3000p/
./wr3000x.sh                            # router console in this terminal
```

Windows: unpack `dist/WR3000X-win64.zip`, run `WR3000X.exe`
(see [README.build.windows.md](README.build.windows.md) for building it).

## Boards

| QEMU machine | Board | RAM / NAND | WAN | Bootloader in the package |
|---|---|---|---|---|
| `cudy-wr3000p` | WR3000P v1 | 512 MB DDR4 / 128 MB | 2.5G, RTL8221B on GMAC2 | OpenWrt (ubootmod) |
| `cudy-wr3000h` | WR3000H v1 | 512 MB (DDR3 BL2) / 128 MB | 2.5G, RTL8221B, PHY reset on the MDIO bus | OpenWrt (ubootmod) |
| `cudy-wr3000s` | WR3000S v1 | 256 MB DDR3 / 128 MB | 1G, MT7531 port 0 | OpenWrt (ubootmod) |
| `cudy-wbr3000uax` | WBR3000UAX v1 | 256 MB DDR3 / 128 MB | 1G, MT7531 port 0 | OpenWrt (ubootmod) |
| `cudy-wr3000u` | WR3000U v1 | 256 MB DDR3 / 256 MB | 1G, MT7531 port 0 | stock Cudy (BL2 v2.7, U-Boot 2022.07, NMBM) |

LED GPIOs differ between boards; LEDs are plain GPIO outputs (`gpio-log=on`
prints them).

## What is emulated

All device models live in `hw/arm/mt7981/` of the QEMU tree
(patches: [`qemu-patches/`](qemu-patches/), base QEMU v10.1.0).

| Block | Model | Notes |
|---|---|---|
| CPU, GIC | 2× Cortex-A53 (EL3/EL2), GICv3, arch timer 13 MHz | CPU1 started by BL31 through TOPMISC SPMC power-on; PSCI is BL31's |
| BootROM | high-level emulation (`cudy_wr3000x.c`) | parses the `SPINAND!` header + GFH `FILE_INFO`, loads BL2 into L2 SRAM, jumps at EL3 |
| DRAM controller | `mt7981_sysctl.c` + generated status table | broadcast mode, RTSWCMD/MRW responses, jitter meter, DQS gating lead/lag, RX data eye: MediaTek's binary DRAM calibration finds real windows, BL2 log is clean (DDR3 and DDR4) |
| Clocks, power, misc | `mt7981_sysctl.c` | sparse register file + special cases (frequency meter, CPU power-on, TRNG v2, eFuse calibration data, IPPC, thermal sensor ≈45 °C, EIP-97 ID) |
| APXGPT | `mt7981_sysctl.c` | 8 general purpose timers (used by the stock Cudy BL2) |
| TOPRGU | `mt7981_toprgu.c` | watchdog with real timeout, SW reset (reboot), reset status kept across reset |
| UART ×3 | QEMU 16550 + MTK extra registers | |
| SPI (IPM) | `mt7981_spim.c` | FIFO + DMA, half-duplex spi-mem mode used by TF-A/U-Boot/Linux |
| SPI-NAND | `spinand.c` | W25N01GV (128 MB) / W25N02KV (256 MB), on-die ECC, ONFI parameter page; backing store = folder of partition dumps (see below) or one raw image with OOB |
| Ethernet | `mt7981_eth.c` | frame engine: QDMA TX (Linux), PDMA RX, PDMA v2 (U-Boot); TSO + checksum offload; LynxI SGMII PCS ×2 |
| Switch | `mt7981_eth.c` | MT7531: paged MDIO access, internal PHY indirect access, MTK special tag (DSA), learning FDB, port matrix, link IRQ → EINT 38 |
| PHYs | `mt7981_eth.c` | MT7531 GPHY ×5, RTL8221B-VB-CG (C45, honours hardware reset on GPIO 3), MT7981 built-in GbE PHY (calibration handshake) |
| GPIO / EINT | `mt7981_pinctrl.c` | buttons (reset/WPS via QOM), LED log, pad levels to board devices |
| USB | QEMU xHCI + MTK IPPC | USB storage etc. can be attached |
| Wi-Fi | `mt7981_wmac.c` | WFDMA rings + emulated WM/WA firmware command interface: firmware loads, both bands come up, hostapd runs, nothing is on the air (scans are empty) |
| Crypto (EIP-97) | ID only | the safexcel driver detects "no packet engine" and disables itself; software crypto is used |

Network backends: any QEMU netdev; ports are selected by netdev id
`lan1`..`lan4`, `wan`. A new `pcap` netdev (`net/pcap.c`, libpcap / Npcap
loaded at run time) attaches a port to a host adapter like a bridged
adapter — used by the Windows launcher.

## Flash (NAND folder)

A NAND folder contains partition dumps without OOB. **Every file whose name
contains `mtdN` becomes partition N**; files are concatenated in order mtd0,
mtd1, … into the full flash, e.g.:

```
cudy_wr3000x.mtd0.BL2.bin   cudy_wr3000x.mtd1.u-boot-env.bin
cudy_wr3000x.mtd2.Factory.bin   cudy_wr3000x.mtd3.bdinfo.bin
cudy_wr3000x.mtd4.FIP.bin   cudy_wr3000x.mtd5.ubi.bin
```

Everything the router writes (settings, sysupgrade, U-Boot env) is written
back into these files. Dumps from a real router (`cat /dev/mtdN >
name.mtdN.label.bin`) can be used directly.

- [`tools/prepare-nand.sh`](tools/prepare-nand.sh) `BOARD [VERSION] [OUTDIR]` — downloads official OpenWrt images (sha256 verified) and builds `nand-BOARD/`; Factory/bdinfo are taken from `factory/`. `wr3000u` uses the stock Cudy BL2/FIP from `wr3000u/` and a stock-layout `sysupgrade.bin`.
- [`tools/mknand.py`](tools/mknand.py) — create / edit images: `create` (BL2, FIP, Factory, bdinfo, UBI from `.itb` or a stock `sysupgrade.bin`), `write --part fip`, `read`, `split`, `join`, `--flash-mb 256`.

## Running

Linux: [`wr3000x.sh`](wr3000x.sh) — `-b BOARD`, `-n NANDDIR`,
`-w bridge|user|none`, `-l isolated|nic|none`, `-p "1 3"` (LAN ports),
`-u DIR` (USB stick from a folder, FAT16), `-L DIR` (console logs),
`-g` (GPIO log), `-d` (unimplemented register log).
Host networking: [`tools/host-bridge.sh`](tools/host-bridge.sh)
(`br0` with the NIC for WAN, isolated `br-wrlan` for LAN — LAN on the real
network would expose the router's DHCP/RA there).

Windows: `WR3000X.exe` — model, NAND folder, WAN/LAN (NAT, "this PC only"
port forwards to LuCI/SSH, or bridge to an adapter via Npcap), USB folder,
log folder, Reset/WPS buttons, power off on `poweroff`, built-in terminal
(VT100, select = copy, right click = paste, Ctrl+Shift+R fits the router
tty to the window).

## Repository layout

```
wr3000x.sh                Linux launcher
build.sh                  QEMU build (Linux)      → README.build.linux.md
build-windows.sh          Windows package (cross) → README.build.windows.md
qemu-patches/             patches on top of QEMU v10.1.0
tools/                    mknand.py, prepare-nand.sh, host-bridge.sh,
                          gen_dramc_table.py (DRAMC status defaults)
windows/                  launcher + terminal (C#), README.txt for the package
tests/                    quick.py (console-driven checks), older pexpect scripts
factory/                  Factory (Wi-Fi EEPROM) and bdinfo (MAC) dumps
wr3000u/                  stock Cudy BL2/FIP dumps + WR3000U sysupgrade.bin
```

## Development notes

- Reference sources used to model the hardware: OpenWrt's Linux 6.18 tree
  (vanilla + OpenWrt patches), U-Boot, mtk-openwrt TF-A, mt76, coreboot's
  MediaTek DRAMC code (MT8192/MT8195, same DRAMC generation).
- `-d unimp` logs every access to registers without explicit modelling —
  the fastest way to find what new firmware polls.
- [`tests/quick.py`](tests/quick.py) starts QEMU with the console on a socket and waits for
  console patterns with hard time limits, e.g.
  `tests/quick.py --qemu "-netdev user,id=wan" 'Starting kernel@60' 'wan: Link is Up@120'`.
- The Windows build uses clang (native TLS): MinGW GCC's emulated TLS made
  guest execution ~30 % slower.

## Limitations

- Wi-Fi radio is silent (no stations, empty scans); the MCU command
  interface answers generically.
- No EIP-97 packet engine, no PCIe devices, PWM/I2C are stubs.
- WR3000H's alternative WAN PHY (Motorcomm YT8821) is not modelled.
- Speed: ~2× slower than the real 1.3 GHz SoC on a typical PC (TCG).
