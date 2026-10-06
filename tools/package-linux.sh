#!/bin/bash
# Build the Linux package dist/MT7981-Router-Emulator-<version>-linux-x86_64.tar.gz
# from an existing QEMU build (./build.sh):
#   - qemu/qemu-system-aarch64 (stripped) with the shared libraries it needs
#     in qemu/lib/ (all but the C library itself), found through
#     RUNPATH=$ORIGIN, so the package runs on other distributions with the
#     same or a newer glibc
#   - mt7981.sh, presets, tools (prepare-nand.sh, mknand.py, host-bridge.sh),
#     tests/quick.py, docs
# No flash folders: they are built with tools/prepare-nand.sh.
#
#   tools/package-linux.sh [QEMU_BINARY]     (default src/qemu/build/...)
#
# Needs patchelf.  For a small dependency set configure QEMU without UI and
# audio, e.g.
#   CONFIGURE_ARGS="--disable-gtk --disable-sdl --disable-opengl --disable-vnc
#     --disable-spice --disable-curl --audio-drv-list=" ./build.sh
set -e
cd "$(dirname "$(readlink -f "$0")")/.."
ROOT=$PWD
BIN=${1:-src/qemu/build/qemu-system-aarch64}
[ -x "$BIN" ] || { echo "no QEMU binary $BIN: run ./build.sh" >&2; exit 1; }
command -v patchelf >/dev/null || { echo "patchelf is needed" >&2; exit 1; }
EMU_VERSION=$(cat VERSION)
APP=MT7981-Router-Emulator
ARCH=$(uname -m)
TGZ=$APP-$EMU_VERSION-linux-$ARCH.tar.gz
PKG=$ROOT/work/linuxpkg/$APP
rm -rf "$PKG" && mkdir -p "$PKG/qemu/lib" "$PKG/usb" "$PKG/tools" "$PKG/tests"

install -m 755 "$BIN" "$PKG/qemu/qemu-system-aarch64"
strip --strip-all "$PKG/qemu/qemu-system-aarch64"
# shared libraries, except the parts of glibc (they must match the system)
ldd "$BIN" | awk '/=> \// { print $1, $3 }' | while read -r name path; do
    case $name in
    ld-linux*|libc.so*|libm.so*|libmvec.so*|libpthread.so*|libdl.so*|\
    librt.so*|libresolv.so*|libutil.so*|libgcc_s.so*) continue ;;
    esac
    cp -L "$path" "$PKG/qemu/lib/$name"
    chmod 644 "$PKG/qemu/lib/$name"
    strip --strip-unneeded "$PKG/qemu/lib/$name"
    patchelf --set-rpath '$ORIGIN' "$PKG/qemu/lib/$name"
done
patchelf --set-rpath '$ORIGIN/lib' "$PKG/qemu/qemu-system-aarch64"

cp mt7981.sh VERSION LICENSE README.md README.ru.md \
   README.build.linux.md README.build.linux.ru.md "$PKG/"
cp -r presets "$PKG/"
cp tools/prepare-nand.sh tools/mknand.py tools/host-bridge.sh "$PKG/tools/"
cp tests/quick.py "$PKG/tests/"
cp usb/README.txt "$PKG/usb/"

# the packaged binary must start without the build tree and without the
# libraries of this machine's QEMU build directory
(cd / && env -i PATH=/usr/bin:/bin "$PKG/qemu/qemu-system-aarch64" -M help |
    grep -q mt7981-router) || { echo "packaged QEMU does not start" >&2; exit 1; }

mkdir -p dist && rm -f "dist/$APP"-*-linux-*.tar.gz
tar -C work/linuxpkg -czf "dist/$TGZ" --owner=0 --group=0 "$APP"
ls -la "dist/$TGZ"
