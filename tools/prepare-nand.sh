#!/bin/bash
# Download official OpenWrt images for an MT7981 device and build a NAND
# folder for the emulator.
#
#   tools/prepare-nand.sh [--stock DIR] [--flash-mb 128|256] PROFILE [VERSION] [OUTDIR]
#
#     PROFILE  OpenWrt device profile, e.g. cudy_wr3000p-v1, cudy_tr3000-v1
#              (see the target's profiles.json).  By default the
#              "PROFILE-ubootmod" images are used (OpenWrt BL2 + U-Boot,
#              UBI layout: BL2, u-boot-env, Factory, bdinfo, FIP, ubi).
#     VERSION  snapshot (default) or a release, e.g. 25.12.5
#     OUTDIR   default: nand-NAME, NAME = PROFILE without the vendor prefix
#              and the -v1 suffix (cudy_wr3000p-v1 -> nand-wr3000p)
#
#     --stock DIR     keep the vendor bootloader: DIR holds dumps of the
#                     vendor BL2 (*mtd0*.bin) and FIP (*mtd4*.bin), and may
#                     hold the OpenWrt *sysupgrade.bin to use
#     --flash-mb N    flash size (default 128; 256 for W25N02KV boards)
#     --local DIR     use the PROFILE-ubootmod images in DIR (own builds:
#                     *-preloader.bin, *-bl31-uboot.fip,
#                     *-squashfs-sysupgrade.itb, *-initramfs-recovery.itb)
#                     instead of downloading them
#
# Factory (Wi-Fi EEPROM) and bdinfo (MAC) are taken from ./factory/ if
# present (*Factory*.bin, *bdinfo*.bin), otherwise left erased/random.
set -e
cd "$(dirname "$(readlink -f "$0")")/.."
STOCK=
LOCAL=
MB=128
while [ $# -gt 0 ]; do
    case $1 in
    --stock) STOCK=$2; shift 2 ;;
    --flash-mb) MB=$2; shift 2 ;;
    --local) LOCAL=$2; shift 2 ;;
    -h|--help) sed -n '2,26p' "$0"; exit 0 ;;
    *) break ;;
    esac
done
P=${1:?usage: tools/prepare-nand.sh [--stock DIR] [--flash-mb N] PROFILE [VERSION] [OUTDIR]}
V=${2:-snapshot}
NAME=${P#*_}; NAME=${NAME%-v1}
OUT=${3:-nand-$NAME}
if [ "$V" = snapshot ]; then
    URL=https://downloads.openwrt.org/snapshots/targets/mediatek/filogic
    BASE=openwrt-mediatek-filogic-$P
else
    URL=https://downloads.openwrt.org/releases/$V/targets/mediatek/filogic
    BASE=openwrt-$V-mediatek-filogic-$P
fi
DL=firmware/$V; mkdir -p "$DL"
FAC=$(ls factory/*Factory*.bin 2>/dev/null | head -1 || true)
BDI=$(ls factory/*bdinfo*.bin 2>/dev/null | head -1 || true)

if [ -n "$STOCK" ]; then
    # vendor BL2/FIP + OpenWrt sysupgrade.bin in UBI kernel/rootfs volumes
    SPFX=$BASE-squashfs-sysupgrade.bin
    BL2=$(ls "$STOCK"/*mtd0*.bin 2>/dev/null | head -1)
    FIP=$(ls "$STOCK"/*mtd4*.bin 2>/dev/null | head -1)
    [ -n "$BL2" ] && [ -n "$FIP" ] || { echo "need $STOCK/*mtd0*.bin and $STOCK/*mtd4*.bin (vendor BL2/FIP dumps)" >&2; exit 1; }
    # a local sysupgrade.bin (releases may not list every device) or download
    SYS=$(ls "$STOCK"/*sysupgrade*.bin 2>/dev/null | grep -- "$V" | head -1 || true)
    [ -n "$SYS" ] || SYS=$(ls "$STOCK"/*sysupgrade*.bin 2>/dev/null | head -1 || true)
    if [ -z "$SYS" ]; then
        wget -q -O "$DL/sha256sums" "$URL/sha256sums"
        wget -q -O "$DL/$SPFX" "$URL/$SPFX" || { rm -f "$DL/$SPFX"; echo "no $SPFX on downloads.openwrt.org; put a sysupgrade.bin into $STOCK/" >&2; exit 1; }
        (cd "$DL" && grep "$SPFX" sha256sums | sha256sum -c --quiet)
        SYS=$DL/$SPFX
    fi
    echo "$P (vendor bootloader): $BL2 + $FIP + $SYS"
    python3 tools/mknand.py --flash-mb "$MB" create -o "$OUT/" --bl2 "$BL2" --fip "$FIP" \
        --sysupgrade "$SYS" ${FAC:+--factory "$FAC"} ${BDI:+--bdinfo "$BDI"} 2>&1 | grep -v '^ubinize'
    exit 0
fi
PFX=$BASE-ubootmod
if [ -n "$LOCAL" ]; then
    img() {
        local f
        f=$(ls "$LOCAL"/*"$P-ubootmod-$1" 2>/dev/null | head -1)
        [ -n "$f" ] || { echo "no *$P-ubootmod-$1 in $LOCAL/" >&2; exit 1; }
        echo "$f"
    }
    BL2=$(img preloader.bin); FIP=$(img bl31-uboot.fip)
    FIT=$(img squashfs-sysupgrade.itb); REC=$(img initramfs-recovery.itb)
    echo "$P: local images from $LOCAL/"
    python3 tools/mknand.py --flash-mb "$MB" create -o "$OUT/" --bl2 "$BL2" --fip "$FIP" \
        --fit "$FIT" --recovery "$REC" \
        ${FAC:+--factory "$FAC"} ${BDI:+--bdinfo "$BDI"} 2>&1 | grep -v '^ubinize'
    exit 0
fi
wget -q -O "$DL/sha256sums" "$URL/sha256sums"
grep -q -- "$PFX-preloader.bin" "$DL/sha256sums" || {
    echo "no $PFX-* images in $URL (wrong profile, or no OpenWrt U-Boot build for it)" >&2; exit 1; }
for f in preloader.bin bl31-uboot.fip squashfs-sysupgrade.itb initramfs-recovery.itb; do
    [ -f "$DL/$PFX-$f" ] || wget -q -O "$DL/$PFX-$f" "$URL/$PFX-$f"
done
(cd "$DL" && grep "$PFX" sha256sums | sha256sum -c --quiet)
python3 tools/mknand.py --flash-mb "$MB" create -o "$OUT/" \
    --bl2 "$DL/$PFX-preloader.bin" --fip "$DL/$PFX-bl31-uboot.fip" \
    --fit "$DL/$PFX-squashfs-sysupgrade.itb" \
    --recovery "$DL/$PFX-initramfs-recovery.itb" \
    ${FAC:+--factory "$FAC"} ${BDI:+--bdinfo "$BDI"} 2>&1 | grep -v '^ubinize'
