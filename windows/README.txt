WR3000X - Cudy WR3000X family router emulator (MediaTek MT7981B)
=================================================================

Emulated boards: Cudy WR3000P v1, WR3000S v1, WR3000U v1, WBR3000UAX v1.
The emulator runs the REAL boot chain, unmodified: BootROM -> BL2 (DDR
training) -> BL31 -> U-Boot -> OpenWrt from the emulated SPI-NAND.

Quick start
-----------
1. Unpack this folder anywhere (path without special characters is best).
2. Run WR3000X.exe, choose the model, press "Power on".
3. A console window opens: that is the router's serial port (115200 8N1).
   Press Enter there to get the OpenWrt shell. Ctrl-A X quits QEMU.
4. Default network: WAN = NAT through this PC (Internet works),
   LAN1 = "This PC only": LuCI at http://127.0.0.1:8080,
   SSH: ssh -p 8022 root@127.0.0.1

Connecting router ports to a real network
-----------------------------------------
Install Npcap from https://npcap.com (tick "WinPcap API-compatible mode").
Then WR3000X.exe lists your network adapters for WAN and LAN1 ("Bridge
to: ..."). The router then appears on that network with its own MAC, like
a VirtualBox bridged adapter. Use a wired adapter - Wi-Fi adapters usually
cannot send frames with foreign MAC addresses.
WARNING: bridging LAN1 to your network puts the router's DHCP server and
IPv6 RA on that network.

NAND (flash) folders
--------------------
nand\              WR3000P   (OpenWrt 25.12.5, OpenWrt U-Boot layout)
nand-wr3000s\      WR3000S
nand-wbr3000uax\   WBR3000UAX
All files whose name contains "mtdN" are joined in order mtd0, mtd1, ...
into the full flash image (e.g. cudy_wr3000x.mtd0.BL2.bin). Changes the
router makes (settings, sysupgrade, U-Boot env) are written back into
these files - keep a copy if you want to return to a clean state.
To use your own dumps (e.g. from "cat /dev/mtdX" on a real device), put
them into a folder and select it as NAND folder.
WR3000U: there is no OpenWrt U-Boot build for it; use dumps of a real
WR3000U (stock Cudy bootloader) as NAND folder.

USB
---
The "USB folder" appears as a USB flash drive (FAT16, <= 500 MB) on the
router's USB port. In OpenWrt install kmod-usb-storage and kmod-fs-vfat,
then: mount /dev/sda1 /mnt

Console logs
------------
With "Log folder" ticked, every "Power on" writes the complete router
console output to a new file logs\console_YYYY-MM-DD_HH-mm-ss.log.

Buttons
-------
"Reset: short" = short press (OpenWrt reboots), "Reset: 10 s" = factory
reset, "WPS button", "Power cycle" = cold reset.

Wi-Fi
-----
Both radios (2.4 / 5 GHz) work from OpenWrt's point of view (hostapd runs,
APs are up), but nothing is on the air: scans return no networks.
