#!/bin/bash
# Download official OpenWrt images for a board and build a NAND folder.
#
#   tools/prepare-nand.sh BOARD [VERSION] [OUTDIR]
#     BOARD    wr3000p | wr3000s | wbr3000uax   (OpenWrt "ubootmod" layout)
#     VERSION  snapshot (default) or a release, e.g. 25.12.5
#     OUTDIR   default: nand-BOARD (nand/ for wr3000p)
#
# Factory (Wi-Fi EEPROM) and bdinfo (MAC) are taken from ./factory/ if
# present (*Factory*.bin, *bdinfo*.bin), otherwise left erased/random.
set -e
cd "$(dirname "$(readlink -f "$0")")/.."
B=${1:?board}; V=${2:-snapshot}
OUT=${3:-$([ "$B" = wr3000p ] && echo nand || echo "nand-$B")}
if [ "$V" = snapshot ]; then
    URL=https://downloads.openwrt.org/snapshots/targets/mediatek/filogic
    PFX=openwrt-mediatek-filogic-cudy_$B-v1-ubootmod
else
    URL=https://downloads.openwrt.org/releases/$V/targets/mediatek/filogic
    PFX=openwrt-$V-mediatek-filogic-cudy_$B-v1-ubootmod
fi
DL=firmware/$V; mkdir -p "$DL"
wget -q -O "$DL/sha256sums" "$URL/sha256sums"
for f in preloader.bin bl31-uboot.fip squashfs-sysupgrade.itb initramfs-recovery.itb; do
    [ -f "$DL/$PFX-$f" ] || wget -q -O "$DL/$PFX-$f" "$URL/$PFX-$f"
done
(cd "$DL" && grep "$PFX" sha256sums | sha256sum -c --quiet)
FAC=$(ls factory/*Factory*.bin 2>/dev/null | head -1 || true)
BDI=$(ls factory/*bdinfo*.bin 2>/dev/null | head -1 || true)
python3 tools/mknand.py create -o "$OUT/" \
    --bl2 "$DL/$PFX-preloader.bin" --fip "$DL/$PFX-bl31-uboot.fip" \
    --fit "$DL/$PFX-squashfs-sysupgrade.itb" \
    --recovery "$DL/$PFX-initramfs-recovery.itb" \
    ${FAC:+--factory "$FAC"} ${BDI:+--bdinfo "$BDI"} 2>&1 | grep -v '^ubinize'
