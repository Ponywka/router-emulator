#!/bin/bash
# Download official OpenWrt images for an MT7981 or MT7986 device and build a
# NAND folder for the emulator.
#
#   tools/prepare-nand.sh [--stock DIR] [--flash-mb 128|256] PROFILE [VERSION] [OUTDIR]
#
#     PROFILE  OpenWrt device profile, e.g. cudy_wr3000p-v1, netis_nx31
#              (see the target's profiles.json).  The OpenWrt BL2 + U-Boot
#              images are used: PROFILE-ubootmod-* if the profile has such a
#              variant, else PROFILE-* (preloader.bin or
#              spim-nand-preloader.bin, bl31-uboot.fip,
#              squashfs-sysupgrade.itb, initramfs-recovery.itb or
#              initramfs.itb).  UBI layout: BL2, u-boot-env, Factory,
#              bdinfo, FIP, ubi (see --no-bdinfo).
#     VERSION  snapshot (default) or a release, e.g. 25.12.5
#     OUTDIR   default: nand-NAME (nor-NAME with --nor), NAME = PROFILE
#              without the vendor prefix and the -v1 suffix
#              (cudy_wr3000p-v1 -> nand-wr3000p)
#
#     --stock DIR     keep the vendor bootloader: DIR holds dumps of the
#                     vendor BL2 (*mtd0*.bin) and FIP (*mtd4*.bin), and may
#                     hold the OpenWrt *sysupgrade.bin to use
#     --flash-mb N    flash size (default 128; 256 for W25N02KV boards)
#     --no-bdinfo     layout without bdinfo (FIP 0x380000, ubi 0x580000;
#                     MACs in Factory), e.g. netis boards
#     --local DIR     use the images in DIR (own builds, same names as
#                     above) instead of downloading them
#     --nor           SPI-NOR board (with --stock): BL2, u-boot-env, Factory,
#                     bdinfo, FIP + OpenWrt sysupgrade.bin in "firmware"
#     --nor-mb N      NOR size (default 16)
#     --soc SOC       mt7981 (default) or mt7986: partition file prefix; an
#                     MT7986 Factory without a dump gets a minimal Wi-Fi
#                     EEPROM (OpenWrt has no default one for that chip)
#
# Factory (Wi-Fi EEPROM) and bdinfo (MAC) are taken from ./factory/ if
# present (*Factory*.bin, *bdinfo*.bin), otherwise left erased/random.
set -e
cd "$(dirname "$(readlink -f "$0")")/.."
STOCK=
LOCAL=
MB=128
LAYOUT=
NOR=
NORMB=16
SOC=mt7981
while [ $# -gt 0 ]; do
    case $1 in
    --stock) STOCK=$2; shift 2 ;;
    --flash-mb) MB=$2; shift 2 ;;
    --local) LOCAL=$2; shift 2 ;;
    --no-bdinfo) LAYOUT=--no-bdinfo; shift ;;
    --nor) NOR=1; shift ;;
    --nor-mb) NORMB=$2; shift 2 ;;
    --soc) SOC=$2; shift 2 ;;
    -h|--help) sed -n '2,36p' "$0"; exit 0 ;;
    *) break ;;
    esac
done
P=${1:?usage: tools/prepare-nand.sh [--stock DIR] [--flash-mb N] PROFILE [VERSION] [OUTDIR]}
V=${2:-snapshot}
NAME=${P#*_}; NAME=${NAME%-v1}
OUT=${3:-$([ -n "$NOR" ] && echo nor || echo nand)-$NAME}
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
[ -z "$LAYOUT" ] || BDI=       # no bdinfo partition
MK=(python3 tools/mknand.py --prefix "$SOC")
EEP=()
[ "$SOC" = mt7986 ] && [ -z "$FAC" ] && EEP=(--wifi-eeprom 7986)

if [ -n "$NOR" ] && [ -z "$STOCK" ]; then
    echo "--nor needs --stock DIR (OpenWrt has no own bootloader images for NOR boards)" >&2
    exit 1
fi
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
    if [ -n "$NOR" ]; then
        "${MK[@]}" nor -o "$OUT/" --size-mb "$NORMB" --bl2 "$BL2" --fip "$FIP" \
            --sysupgrade "$SYS" ${FAC:+--factory "$FAC"} ${BDI:+--bdinfo "$BDI"}
        exit 0
    fi
    "${MK[@]}" --flash-mb "$MB" $LAYOUT create -o "$OUT/" --bl2 "$BL2" --fip "$FIP" \
        --sysupgrade "$SYS" ${FAC:+--factory "$FAC"} ${BDI:+--bdinfo "$BDI"} "${EEP[@]}" 2>&1 | grep -v '^ubinize'
    exit 0
fi
# image name variants, first match wins
PRE="ubootmod-preloader.bin preloader.bin spim-nand-preloader.bin"
FIPS="ubootmod-bl31-uboot.fip bl31-uboot.fip spim-nand-bl31-uboot.fip"
FITS="ubootmod-squashfs-sysupgrade.itb squashfs-sysupgrade.itb"
RECS="ubootmod-initramfs-recovery.itb initramfs-recovery.itb initramfs.itb"
if [ -n "$LOCAL" ]; then
    # own builds: file names may carry a version or not
    pick() {
        local s f
        for s in $1; do
            f=$(ls "$LOCAL"/*"$P-$s" 2>/dev/null | head -1)
            [ -n "$f" ] && { echo "$f"; return; }
        done
        [ "$2" = optional ] || { echo "no *$P-{${1// /,}} in $LOCAL/" >&2; exit 1; }
    }
    echo "$P: local images from $LOCAL/"
else
    wget -q -O "$DL/sha256sums" "$URL/sha256sums"
    pick() {
        local s f
        for s in $1; do
            f=$BASE-$s
            if grep -q -- " \*$f\$" "$DL/sha256sums"; then
                [ -f "$DL/$f" ] || wget -q -O "$DL/$f" "$URL/$f"
                (cd "$DL" && grep -- " \*$f\$" sha256sums | sha256sum -c --quiet)
                echo "$DL/$f"
                return
            fi
        done
        [ "$2" = optional ] || { echo "no $BASE-{${1// /,}} in $URL (wrong profile, or no OpenWrt U-Boot build for it)" >&2; exit 1; }
    }
fi
BL2=$(pick "$PRE"); FIP=$(pick "$FIPS"); FIT=$(pick "$FITS"); REC=$(pick "$RECS" optional)
[ -n "$BL2" ] && [ -n "$FIP" ] && [ -n "$FIT" ] || exit 1
echo "$P: $(basename "$BL2"), $(basename "$FIP"), $(basename "$FIT")${REC:+, $(basename "$REC")}"
"${MK[@]}" --flash-mb "$MB" $LAYOUT create -o "$OUT/" --bl2 "$BL2" --fip "$FIP" \
    --fit "$FIT" ${REC:+--recovery "$REC"} \
    ${FAC:+--factory "$FAC"} ${BDI:+--bdinfo "$BDI"} "${EEP[@]}" 2>&1 | grep -v '^ubinize'
