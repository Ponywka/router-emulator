#!/bin/bash
# Run the Cudy WR3000X family emulator (WR3000P/S/U, WBR3000UAX).
#
#   ./wr3000x.sh [options] [-- extra qemu args]
#
# Options:
#   -b BOARD       cudy-wr3000p (default), cudy-wr3000s, cudy-wr3000u,
#                  cudy-wbr3000uax
#   -n DIR         NAND directory with partition dumps (*mtdN*, e.g.
#                  cudy_wr3000x.mtd0.BL2.bin)
#                  (default: ./nand), concatenated in mtd order
#   -w MODE        WAN: bridge (tap wr-wan on br0, default), user (NAT via
#                  QEMU, router WAN gets 10.0.2.15), none
#   -l MODE        LAN: isolated (taps on br-wrlan, host 192.168.1.2, default)
#                  nic (taps on br0 = the physical network!), none
#   -p PORTS       LAN ports to connect, e.g. "1" (default) or "1 3".
#                  Connecting several ports to the same host bridge creates
#                  a loop (the router bridges its LAN ports), so use one
#                  unless you add separate host bridges yourself.
#   -u DIR         export DIR as a USB flash drive (FAT16, max 500MB, read-write, QEMU
#                  vvfat) on the router's USB port (default: ./usb if it
#                  exists; "-u none" disables).  Needs kmod-usb-storage +
#                  kmod-fs-vfat in OpenWrt; the stick shows up as /dev/sda1
#   -L DIR         console log folder (default: ./logs, "-L none" disables);
#                  every start writes console_YYYY-MM-DD_HH-MM-SS.log
#   -m MONITOR     QEMU monitor socket path (default: ./work/monitor.sock)
#   -g             print GPIO/LED changes
#   -d             debug: log unimplemented register accesses to work/qemu.log
#
# Console: serial (UART0) on this terminal.  Exit QEMU with Ctrl-A X.
# Buttons: socat - UNIX-CONNECT:work/monitor.sock, then
#   qom-set /machine/pinctrl reset-button true / false
set -e
cd "$(dirname "$(readlink -f "$0")")"
ROOT=$PWD
QEMU=$ROOT/src/qemu/build/qemu-system-aarch64
BOARD=cudy-wr3000p
NAND=$ROOT/nand
WAN=bridge
LAN=isolated
PORTS="1"
USBDIR=$ROOT/usb
LOGDIR=$ROOT/logs
MON=$ROOT/work/monitor.sock
EXTRA=()
GPIO=
DEBUG=()

while getopts "b:n:w:l:p:u:L:m:gdh" o; do
    case $o in
    b) BOARD=$OPTARG ;;
    n) NAND=$(readlink -f "$OPTARG") ;;
    w) WAN=$OPTARG ;;
    l) LAN=$OPTARG ;;
    p) PORTS=$OPTARG ;;
    u) USBDIR=$OPTARG ;;
    L) LOGDIR=$OPTARG ;;
    m) MON=$OPTARG ;;
    g) GPIO=,gpio-log=on ;;
    d) DEBUG=(-d unimp,guest_errors -D "$ROOT/work/qemu.log") ;;
    *) sed -n '2,33p' "$0"; exit 1 ;;
    esac
done
shift $((OPTIND - 1))
[ "$1" = "--" ] && shift
EXTRA=("$@")
mkdir -p "$ROOT/work"

if [ ! -x "$QEMU" ]; then
    echo "QEMU not built: run ./build.sh" >&2
    exit 1
fi

NET=()
lan_br=
case $LAN in
isolated) lan_br=br-wrlan ;;
nic) lan_br=br0
     echo "WARNING: router LAN ports are bridged to the physical network;" \
          "its DHCP/RA servers will be visible there." >&2 ;;
none) ;;
*) echo "bad -l $LAN" >&2; exit 1 ;;
esac

if [ "$WAN" = bridge ] || [ -n "$lan_br" ]; then
    # (re)create taps; needs root once per boot of the host
    if ! ip link show wr-wan >/dev/null 2>&1 ||
       { [ -n "$lan_br" ] && [ "$(basename "$(readlink -f /sys/class/net/wr-lan1/master 2>/dev/null)")" != "$lan_br" ]; }; then
        sudo "$ROOT/tools/host-bridge.sh" taps br0 "$USER" "${lan_br:-br-wrlan}"
    fi
fi

case $WAN in
bridge) NET+=(-netdev tap,id=wan,ifname=wr-wan,script=no,downscript=no) ;;
user) NET+=(-netdev user,id=wan) ;;
none) ;;
*) echo "bad -w $WAN" >&2; exit 1 ;;
esac
if [ -n "$lan_br" ]; then
    for i in $PORTS; do
        NET+=(-netdev tap,id=lan$i,ifname=wr-lan$i,script=no,downscript=no)
    done
fi

USB=()
if [ "$USBDIR" != none ] && [ -d "$USBDIR" ]; then
    d=$(readlink -f "$USBDIR")
    USB=(-blockdev "driver=vvfat,node-name=usbstick,dir=${d//,/,,},rw=on,fat-type=16"
         -device usb-storage,drive=usbstick,removable=on)
fi

CON=(-monitor "unix:$MON,server,nowait")
if [ "$LOGDIR" != none ]; then
    mkdir -p "$LOGDIR"
    LOG=$(readlink -f "$LOGDIR")/console_$(date +%Y-%m-%d_%H-%M-%S).log
    echo "console log: $LOG" >&2
    CON+=(-chardev "stdio,id=con,mux=on,signal=off,logfile=${LOG//,/,,},logappend=off"
          -serial chardev:con -mon chardev=con)
fi

rm -f "$MON"
"$QEMU" -M "$BOARD,nand-dir=$NAND$GPIO" -nographic \
    "${CON[@]}" \
    "${NET[@]}" "${USB[@]}" "${DEBUG[@]}" "${EXTRA[@]}"
rc=$?
# make the log readable: no escape sequences, no "\r\r\n" double breaks
if [ -n "$LOG" ] && [ -f "$LOG" ]; then
    sed -i -e 's/\x1b\[[0-9;?]*[A-Za-z]//g' -e 's/\r//g' "$LOG"
fi
exit $rc
