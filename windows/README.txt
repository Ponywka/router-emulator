WR3000X - Cudy WR3000X family router emulator (MediaTek MT7981B)
=================================================================

Emulated boards: Cudy WR3000P v1, WR3000S v1, WR3000U v1, WBR3000UAX v1.
The emulator runs the REAL boot chain, unmodified: BootROM -> BL2 (DDR
training) -> BL31 -> U-Boot -> OpenWrt from the emulated SPI-NAND.

Quick start
-----------
1. Unpack this folder anywhere (path without special characters is best).
2. Run WR3000X.exe, choose the model, press "Power on".
3. A terminal window opens: that is the router's serial port (115200 8N1).
   Press Enter there to get the OpenWrt shell. Arrow keys, Home/End etc.
   work (also in the U-Boot menu). Select text with the mouse = copy,
   right click or Shift+Insert = paste, mouse wheel / Shift+PgUp = scroll
   back. Closing the window powers the router off. Ctrl-A C opens the
   QEMU monitor.
   A serial console cannot tell the router its size, so Linux assumes
   80x24 (same as with PuTTY on a real router). Press Ctrl+Shift+R (or
   click "Fit router console to window") to run "resize" in the router:
   mc, top, vi then use the whole window. The window can be resized;
   press Ctrl+Shift+R again afterwards.
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
nand-wr3000h\      WR3000H
nand-wr3000u\      WR3000U   (256 MB flash, stock Cudy BL2/U-Boot + OpenWrt
                   25.12.5 in the stock layout: UBI kernel/rootfs)
All files whose name contains "mtdN" are joined in order mtd0, mtd1, ...
into the full flash image (e.g. cudy_wr3000x.mtd0.BL2.bin). Changes the
router makes (settings, sysupgrade, U-Boot env) are written back into
these files - keep a copy if you want to return to a clean state.
To use your own dumps (e.g. from "cat /dev/mtdX" on a real device), put
them into a folder and select it as NAND folder.

USB
---
The "USB folder" appears as a USB flash drive (FAT16, <= 500 MB) on the
router's USB port. In OpenWrt install kmod-usb-storage and kmod-fs-vfat,
then: mount /dev/sda1 /mnt

Console logs
------------
With "Log folder" ticked, every "Power on" writes the complete router
console output to a new file logs\console_YYYY-MM-DD_HH-mm-ss.log
(plain text, escape sequences removed, Windows line endings).

reboot / poweroff
-----------------
"reboot" in OpenWrt restarts the router (BootROM -> BL2 -> ... again).
A real MT7981 cannot switch itself off: on "poweroff" its firmware prints
"Power-down unsupported." and reboots. With "Turn the emulator off on
poweroff" ticked (default) the emulator stops instead, like pulling the
power plug.

Buttons
-------
"Reset: short" = short press (OpenWrt reboots), "Reset: 10 s" = factory
reset, "WPS button", "Power cycle" = cold reset.

Wi-Fi
-----
Both radios (2.4 / 5 GHz) work from OpenWrt's point of view (hostapd runs,
APs are up), but nothing is on the air: scans return no networks.
