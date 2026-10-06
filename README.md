# MT7981 Router Emulator (MediaTek MT7981B / Filogic 820)

Version **0.1** ([`VERSION`](VERSION)) · **English** · [Русский](README.ru.md) · Build: [Linux](README.build.linux.md) · [Windows](README.build.windows.md)

A QEMU machine, `mt7981-router`, that emulates an MT7981B router board at
the hardware level. The board hardware (Ethernet PHYs/switch, flash, RAM
type and size, USB) is configurable, so one machine covers many devices;
**board presets** (`presets/*.ini`) describe concrete routers. Firmware
runs **unmodified**, through the same boot chain as on a real device:

```
BootROM (emulated) → BL2 (MediaTek preloader, DDR training) → BL31 (TF-A)
→ U-Boot → OpenWrt (kernel + rootfs from UBI on SPI-NAND)
```

Both OpenWrt's own bootloader ("ubootmod" layout) and vendor bootloaders
(BL2/FIP dumped from real devices, with NMBM) boot. Everything the
firmware needed was adapted on the emulator side; no firmware is patched.

## Quick start

Linux: [README.build.linux.md](README.build.linux.md), then

```bash
tools/prepare-nand.sh cudy_wr3000p-v1 25.12.5   # official images -> nand-wr3000p/
./mt7981.sh -P list                             # board presets
./mt7981.sh -P cudy-wr3000p-v1                  # router console in this terminal
```

Windows: download the release zip (or build it, see
[README.build.windows.md](README.build.windows.md)), unpack, run `MT7981.exe`.
The release contains no NAND folders: create them with
`tools/prepare-nand.sh` (Linux or WSL) and copy them next to `MT7981.exe`,
or use dumps of a real router.

## Board hardware (machine options)

`-M mt7981-router,nand-dir=DIR,<options>` and `-m <RAM size>`:

| Option | Values | Meaning |
|---|---|---|
| `gmac0` | `mt7531` · `rtl8221b` · `yt8821` · `none` | what GMAC0 (mac@0, SGMII0) is wired to |
| `ports` | e.g. `wan:lan1:lan2:lan3:lan4` | netdev ids of MT7531 ports 0..4 (`-` = unused) |
| `gmac0-port`, `gmac1-port` | netdev id | port of a PHY attached directly to a GMAC |
| `gmac1` | `rtl8221b` · `yt8821` · `gphy` · `none` | GMAC1 (mac@1); `gphy` = MT7981 built-in 1G PHY |
| `gmac0-reset-gpio`, `gmac1-reset-gpio` | GPIO, `-1` (default) | hardware reset line of a 2.5G PHY; not wired by default (a missing line is harmless, a wrong one could hold the PHY in reset) |
| `nand` | `128` · `256` | W25N01GV / W25N02KV SPI-NAND |
| `ddr` | `ddr3` · `ddr4` | soldered DRAM type: a BL2 built for the other type stops the machine, as DRAM init fails on a real board |
| `usb-port` | `none` · `2` · `3` | USB connector (USB 3.0: devices attach at SuperSpeed) |
| `reset-gpio`, `wps-gpio` | GPIO | buttons (QOM `/machine/pinctrl` `reset-button`, `wps-button`) |
| `reset-active-high`, `wps-active-high` | `on` | button reads 1 when pressed (default: active low) |
| `reset-hold` | ms | power on with reset held (U-Boot TFTP recovery) |
| `gpio-log` | `on` | print GPIO output changes (LEDs) |

Network ports are QEMU netdevs with the ids used above (`wan`, `lan1`, …).
Launcher-only preset keys: `lan-ip` (router LAN address, default
192.168.1.1) and `lan-forwards` (default `8080:80,8443:443,8022:22`, PC
port:router port) for LAN1 "this PC only": a QEMU user-mode network in the
router's /24 with `restrict=on` and these forwards from 127.0.0.1.

## Board presets

A preset is an INI file: `name`, `description`, `ram` (MB), `nand-dir`,
build-only keys (`openwrt=` OpenWrt profile, `openwrt-local=` own build,
`openwrt-stock=` vendor bootloader dumps) and machine options. Both
launchers use them; the Windows launcher has an editor for them (Ethernet,
switch port labels, RAM type/size, NAND, USB, button GPIOs with an
"active high" tick — unticked = active low, the default and what most
routers use; saving from the editor drops `;` comments).

| Preset | Ethernet | RAM | NAND | USB | Bootloader |
|---|---|---|---|---|---|
| Cudy WR3000P v1 | 2.5G WAN RTL8221B + 4×1G MT7531 | DDR4 512 MB | 128 MB | 2.0 | OpenWrt |
| Cudy WR3000H v1 | 2.5G WAN RTL8221B + 4×1G MT7531 | DDR3 512 MB | 128 MB | – | OpenWrt |
| Cudy WR3000S v1, WR3000E v1 | 5×1G MT7531 (WAN = port 0) | DDR3 256 MB | 128 MB | – | OpenWrt |
| Cudy WBR3000UAX v1 | 5×1G MT7531 (WAN = port 0) | DDR3 256 MB | 128 MB | 3.0 | OpenWrt |
| Cudy WR3000U v1 | 5×1G MT7531 (WAN = port 0) | DDR3 256 MB | 256 MB | – | vendor (NMBM) |
| Cudy TR3000 v1 | 2.5G WAN RTL8221B on GMAC0 + 1G LAN built-in PHY | DDR3 512 MB | 128 MB | 3.0 | OpenWrt |
| Cudy TR3000 256MB v1 | same, no switch | DDR3 512 MB | 256 MB | 3.0 | vendor (NMBM) |
| Cudy M3000 v1 / v2 (RTL8221B) | 2.5G WAN RTL8221B + 1G LAN built-in PHY | DDR3 256 MB | 128 MB | – | OpenWrt (own build) |
| Cudy M3000 v2 (YT8821) | 2.5G WAN Motorcomm YT8821 + 1G LAN built-in PHY | DDR3 256 MB | 128 MB | – | OpenWrt (own build) |
| Netis NX30 V2, NX31 | 1G WAN built-in PHY + 3×1G LAN MT7531 | DDR3 256 MB | 128 MB | – | OpenWrt |
| Netis NX32U | 4×1G MT7531 (WAN = port 0) | DDR3 256 MB | 128 MB | 3.0 | OpenWrt |

Netis boards have no bdinfo partition (FIP at 0x380000, ubi at 0x580000;
MACs in Factory): presets set `openwrt-no-bdinfo=1`.

## What is emulated

All device models live in `hw/arm/mt7981/` of the QEMU tree
(patches: [`qemu-patches/`](qemu-patches/), base QEMU v10.1.0).

| Block | Model | Notes |
|---|---|---|
| CPU, GIC | 2× Cortex-A53 (EL3/EL2), GICv3, arch timer 13 MHz | CPU1 started by BL31 through TOPMISC SPMC power-on; PSCI is BL31's |
| BootROM | high-level emulation (`mt7981_router.c`) | parses the `SPINAND!` header + GFH `FILE_INFO`, loads BL2 into L2 SRAM, jumps at EL3 |
| DRAM controller | `mt7981_sysctl.c` + generated status table | broadcast mode, RTSWCMD/MRW responses, jitter meter, DQS gating lead/lag, RX data eye: MediaTek's binary DRAM calibration finds real windows, BL2 log is clean (DDR3 and DDR4); `DDRCOMMON0` DDR3EN/DDR4EN checked against `ddr=` |
| Clocks, power, misc | `mt7981_sysctl.c` | sparse register file + special cases (frequency meter, CPU power-on, TRNG v2, eFuse calibration data, IPPC, thermal sensor ≈45 °C, EIP-97 ID, WED reset bits) |
| APXGPT | `mt7981_sysctl.c` | 8 general purpose timers (used by vendor BL2s) |
| TOPRGU | `mt7981_toprgu.c` | watchdog with real timeout, SW reset (reboot), reset status kept across reset |
| UART ×3 | QEMU 16550 + MTK extra registers | |
| SPI (IPM) | `mt7981_spim.c` | FIFO + DMA, half-duplex spi-mem mode used by TF-A/U-Boot/Linux |
| SPI-NAND | `spinand.c` | W25N01GV (2048+64) / W25N02KV (2048+128), on-die ECC, ONFI parameter page; backing store = folder of partition dumps (see below) or one raw image with OOB |
| Ethernet | `mt7981_eth.c` | frame engine: QDMA TX (Linux), PDMA RX, PDMA v2 (U-Boot); TSO + checksum offload; LynxI SGMII PCS ×2; any combination of switch / PHYs on the two GMACs |
| Switch | `mt7981_eth.c` | MT7531: paged MDIO access, internal PHY indirect access, MTK special tag (DSA), learning FDB, port matrix, link IRQ → EINT 38 |
| PHYs | `mt7981_eth.c` | MT7531 GPHY ×5; RTL8221B-VB-CG (C45, temperature sensor); Motorcomm YT8821 (extended registers, UTP/SerDes spaces, 2.5G status); MT7981 built-in GbE PHY (calibration handshake); optional hardware reset GPIOs |
| GPIO / EINT | `mt7981_pinctrl.c` | buttons (reset/WPS, `reset-hold-ms` for timed presses), LED log, pad levels to board devices |
| USB | QEMU xHCI + MTK IPPC | USB 2.0 / 3.0 connector |
| Wi-Fi | `mt7981_wmac.c` | WFDMA rings + emulated WM/WA firmware command interface: firmware loads, both bands come up, hostapd runs, nothing is on the air (scans are empty) |
| Crypto (EIP-97) | ID only | the safexcel driver detects "no packet engine" and disables itself; software crypto is used |
| WED | reset bits only | with `wed_enable=1` mt7915e detects the missing WO MCU and runs without offload |

A new `pcap` netdev (`net/pcap.c`, libpcap / Npcap loaded at run time)
attaches a port to a host adapter like a bridged adapter — used by the
Windows launcher.

## Flash (NAND folder)

A NAND folder contains partition dumps without OOB. **Every file whose name
contains `mtdN` becomes partition N**; files are concatenated in order mtd0,
mtd1, … into the full flash, e.g. `mt7981.mtd0.BL2.bin`,
`mt7981.mtd1.u-boot-env.bin`, `mt7981.mtd2.Factory.bin`,
`mt7981.mtd3.bdinfo.bin`, `mt7981.mtd4.FIP.bin`, `mt7981.mtd5.ubi.bin`.

Everything the router writes (settings, sysupgrade, U-Boot env) is written
back into these files. Dumps from a real router (`cat /dev/mtdN >
name.mtdN.label.bin`) can be used directly.

- [`tools/prepare-nand.sh`](tools/prepare-nand.sh) `[--stock DIR | --local DIR] [--flash-mb N] [--no-bdinfo] PROFILE [VERSION] [OUTDIR]` — builds a NAND folder for an OpenWrt device profile: downloads the official OpenWrt U-Boot images (`PROFILE-ubootmod-*` or `PROFILE-*`, sha256 verified), or uses own builds (`--local`), or keeps a vendor BL2/FIP (`--stock`) with the OpenWrt `sysupgrade.bin` in the vendor layout; `--no-bdinfo` for boards without a bdinfo partition. Factory/bdinfo come from `factory/`.
- [`tools/mknand.py`](tools/mknand.py) — create / edit images: `create` (BL2, FIP, Factory, bdinfo, UBI from `.itb` or a `sysupgrade.bin`), `write --part fip`, `read`, `split`, `join`, `--flash-mb 256`, `--no-bdinfo`.

## Running

Linux: [`mt7981.sh`](mt7981.sh) — `-P PRESET` (`-P list`), `-o OPTS`
(override machine options, `ram=`), `-n NANDDIR`, `-w bridge|user|none`,
`-l isolated|nic|user|none` (`user` = this PC only, forwards from the
preset), `-p "1 3"` (LAN ports), `-u DIR` (USB stick from a
folder, FAT16), `-L DIR` (console logs), `-g` (GPIO log), `-R` (power on
with reset held 10 s → TFTP recovery), `-d` (unimplemented register log).
Host networking: [`tools/host-bridge.sh`](tools/host-bridge.sh) (`br0`
with the NIC for WAN, isolated `br-wrlan` for LAN — LAN on the real
network would expose the router's DHCP/RA there).

Windows: `MT7981.exe` — board preset (with editor: New / Edit / Save /
Save as / Delete), NAND folder, WAN/LAN (NAT, "this PC only" port forwards
to LuCI/SSH, or bridge to an adapter via Npcap), USB folder, log folder,
Reset/WPS buttons, "Power + Reset: 10 s" (TFTP recovery), power off on
`poweroff`, built-in terminal (VT100, PuTTY-like 80×24 default with cell
snapping, select = copy, right click = paste, Ctrl+Shift+R fits the router
tty to the window), interface language switched on the fly
([`languages/*.ini`](languages/): English, Русский; add a language by
copying `en.ini`; [`tools/gen-lang-en.py`](tools/gen-lang-en.py)
regenerates `en.ini` from the sources, `--check` lists untranslated keys).

## Repository layout

```
mt7981.sh                 Linux launcher
presets/                  board presets (*.ini)
build.sh                  QEMU build (Linux)      → README.build.linux.md
build-windows.sh          Windows package (cross) → README.build.windows.md
qemu-patches/             patches on top of QEMU v10.1.0
tools/                    prepare-nand.sh, mknand.py, host-bridge.sh,
                          gen_dramc_table.py (DRAMC status defaults)
windows/                  launcher, preset editor, terminal (C#),
                          README.txt for the package
languages/                launcher texts (en.ini, ru.ini)
tests/                    quick.py (console-driven checks), powercut.py
factory/                  Factory (Wi-Fi EEPROM) and bdinfo (MAC) dumps
wr3000u/, tr3000/         vendor BL2/FIP dumps (not in git)
m3000/                    own OpenWrt builds (not in git)
```

## Development notes

- Reference sources used to model the hardware: OpenWrt's Linux tree
  (vanilla + OpenWrt patches), U-Boot, mtk-openwrt TF-A, mt76, coreboot's
  MediaTek DRAMC code (MT8192/MT8195, same DRAMC generation).
- `-d unimp` logs every access to registers without explicit modelling —
  the fastest way to find what new firmware polls.
- [`tests/quick.py`](tests/quick.py) starts QEMU with the console on a
  socket and waits for console patterns with hard time limits, e.g.
  `tests/quick.py -P cudy-tr3000-v1 --qemu="-netdev user,id=wan" 'Starting kernel@60' 'eth0: Link is Up@120'`.
  [`tests/powercut.py`](tests/powercut.py) quits/resets QEMU at random
  moments and checks that BL2 still loads FIP.
- The Windows build uses clang (native TLS): MinGW GCC's emulated TLS made
  guest execution ~30 % slower; it is also PGO-optimised (5–17 % on real
  Windows), and the launcher keeps QEMU out of Windows 11 efficiency mode.
  Profile (`perf`): most time goes to TCG's translation-block lookup, the
  device models are negligible; -O3/LTO gave nothing. QEMU already runs
  each of the two guest CPUs in its own host thread (MTTCG); one guest
  thread cannot be spread over more host cores.
- OpenWrt marks the overlay "ready" only at the end of boot; rebooting
  earlier makes fstools wipe it (same as on hardware).

## Limitations

- Wi-Fi radio is silent (no stations, empty scans); the MCU command
  interface answers generically. WED offload is not emulated.
- PHY interrupts are not generated (link changes after boot are seen only
  by polling drivers).
- No EIP-97 packet engine, no PCIe devices, PWM/I2C are stubs.
- Speed: ~2× slower than the real 1.3 GHz SoC on a typical PC (TCG).

## License

GPL-2.0-or-later, like QEMU (the emulator is a set of QEMU patches plus
tools around it): see [LICENSE](LICENSE). Firmware images are not part of
this repository; OpenWrt and vendor firmware keep their own licenses.
