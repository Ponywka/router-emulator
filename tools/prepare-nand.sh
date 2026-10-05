#!/bin/bash
# Download official OpenWrt images for a board and build a NAND folder.
#
#   tools/prepare-nand.sh BOARD [VERSION] [OUTDIR]
#     BOARD    wr3000p | wr3000h | wr3000s | wbr3000uax  (OpenWrt "ubootmod"
#              layout), or wr3000u (stock Cudy layout: needs dumps of the
#              Cudy bootloader in ./wr3000u/: *mtd0*BL2*.bin, *mtd4*FIP*.bin)
#     VERSION  snapshot (default) or a release, e.g. 25.12.5
#     OUTDIR   default: nand-BOARD
#
# Factory (Wi-Fi EEPROM) and bdinfo (MAC) are taken from ./factory/ if
# present (*Factory*.bin, *bdinfo*.bin), otherwise left erased/random.
set -e
cd "$(dirname "$(readlink -f "$0")")/.."
B=${1:?board}; V=${2:-snapshot}
OUT=${3:-nand-$B}
if [ "$V" = snapshot ]; then
    URL=https://downloads.openwrt.org/snapshots/targets/mediatek/filogic
    PFX=openwrt-mediatek-filogic-cudy_$B-v1-ubootmod
else
    URL=https://downloads.openwrt.org/releases/$V/targets/mediatek/filogic
    PFX=openwrt-$V-mediatek-filogic-cudy_$B-v1-ubootmod
fi
DL=firmware/$V; mkdir -p "$DL"
FAC=$(ls factory/*Factory*.bin 2>/dev/null | head -1 || true)
BDI=$(ls factory/*bdinfo*.bin 2>/dev/null | head -1 || true)

if [ "$B" = wr3000u ]; then
    # stock layout: Cudy BL2/FIP + OpenWrt sysupgrade.bin in UBI kernel/rootfs
    SPFX=${PFX%-ubootmod}-squashfs-sysupgrade.bin
    BL2=$(ls wr3000u/*mtd0*.bin 2>/dev/null | head -1)
    FIP=$(ls wr3000u/*mtd4*.bin 2>/dev/null | head -1)
    [ -n "$BL2" ] && [ -n "$FIP" ] || { echo "need wr3000u/*mtd0*.bin and wr3000u/*mtd4*.bin (stock Cudy BL2/FIP dumps)" >&2; exit 1; }
    # OpenWrt sysupgrade.bin: local copy in wr3000u/ (official releases may
    # not list the WR3000U), otherwise download + verify
    SYS=$(ls wr3000u/*wr3000u*sysupgrade*.bin 2>/dev/null | grep -- "$V" | head -1 || true)
    [ -n "$SYS" ] || SYS=$(ls wr3000u/*sysupgrade*.bin 2>/dev/null | head -1 || true)
    if [ -z "$SYS" ]; then
        wget -q -O "$DL/sha256sums" "$URL/sha256sums"
        wget -q -O "$DL/$SPFX" "$URL/$SPFX" || { rm -f "$DL/$SPFX"; echo "no $SPFX on downloads.openwrt.org; put a sysupgrade.bin into wr3000u/" >&2; exit 1; }
        (cd "$DL" && grep "$SPFX" sha256sums | sha256sum -c --quiet)
        SYS=$DL/$SPFX
    fi
    echo "WR3000U: $BL2 + $FIP + $SYS"
    python3 tools/mknand.py --flash-mb 256 create -o "$OUT/" --bl2 "$BL2" --fip "$FIP" \
        --sysupgrade "$SYS" ${FAC:+--factory "$FAC"} ${BDI:+--bdinfo "$BDI"} 2>&1 | grep -v '^ubinize'
    exit 0
fi
wget -q -O "$DL/sha256sums" "$URL/sha256sums"
for f in preloader.bin bl31-uboot.fip squashfs-sysupgrade.itb initramfs-recovery.itb; do
    [ -f "$DL/$PFX-$f" ] || wget -q -O "$DL/$PFX-$f" "$URL/$PFX-$f"
done
(cd "$DL" && grep "$PFX" sha256sums | sha256sum -c --quiet)
python3 tools/mknand.py create -o "$OUT/" \
    --bl2 "$DL/$PFX-preloader.bin" --fip "$DL/$PFX-bl31-uboot.fip" \
    --fit "$DL/$PFX-squashfs-sysupgrade.itb" \
    --recovery "$DL/$PFX-initramfs-recovery.itb" \
    ${FAC:+--factory "$FAC"} ${BDI:+--bdinfo "$BDI"} 2>&1 | grep -v '^ubinize'
