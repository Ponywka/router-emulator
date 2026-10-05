#!/bin/bash
# Build the Windows package dist/WR3000X-win64.zip
#   - QEMU (with qemu-patches/) cross-compiled in QEMU's Fedora MinGW image
#   - WR3000X.exe launcher (C#, needs mono-mcs)
#   - fresh NAND folders for OpenWrt $VERSION (default 25.12.5)
set -e
cd "$(dirname "$(readlink -f "$0")")"
ROOT=$PWD
VERSION=${VERSION:-25.12.5}
[ -d src/qemu/.git ] || ./build.sh
SUDO=; docker info >/dev/null 2>&1 || SUDO=sudo
$SUDO docker image inspect qemu-win64-cross >/dev/null 2>&1 ||
    $SUDO docker build -t qemu-win64-cross \
        -f src/qemu/tests/docker/dockerfiles/fedora-win64-cross.docker \
        src/qemu/tests/docker/dockerfiles
PKG=$ROOT/work/winpkg/WR3000X
rm -rf "$PKG" && mkdir -p "$PKG/qemu" "$PKG/usb"
$SUDO docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp \
    -v "$ROOT/src/qemu:/src" -v "$PKG/qemu:/out" qemu-win64-cross bash -c '
set -e
mkdir -p /src/build-win && cd /src/build-win
[ -f build.ninja ] || ../configure --cross-prefix=x86_64-w64-mingw32- \
    --target-list=aarch64-softmmu --enable-slirp --enable-fdt=internal \
    --disable-docs --disable-werror --disable-gtk --disable-sdl \
    --disable-vnc --disable-spice --disable-opengl --disable-curl \
    --disable-guest-agent --disable-tools
ninja
SR=/usr/x86_64-w64-mingw32/sys-root/mingw/bin
cp qemu-system-aarch64.exe /out/
cp $(find subprojects -name "*.dll") /out/
todo=$(ls /out/*.exe /out/*.dll)
while [ -n "$todo" ]; do next=""
  for f in $todo; do
    for d in $(x86_64-w64-mingw32-objdump -p $f | awk "/DLL Name/ {print \$3}"); do
      if [ -f $SR/$d ] && [ ! -f /out/$d ]; then cp $SR/$d /out/; next="$next /out/$d"; fi
    done
  done
  todo=$next
done
x86_64-w64-mingw32-strip --strip-unneeded /out/*.exe /out/*.dll'
mcs -target:winexe -platform:anycpu -out:"$PKG/WR3000X.exe" \
    -r:System.Windows.Forms.dll -r:System.Drawing.dll windows/WR3000X.cs windows/Terminal.cs
cp windows/README.txt "$PKG/"
cp usb/README.txt "$PKG/usb/"
mkdir -p "$PKG/logs"
tools/prepare-nand.sh wr3000p "$VERSION" "$PKG/nand"
tools/prepare-nand.sh wr3000s "$VERSION" "$PKG/nand-wr3000s"
tools/prepare-nand.sh wbr3000uax "$VERSION" "$PKG/nand-wbr3000uax"
mkdir -p dist && rm -f dist/WR3000X-win64.zip
(cd work/winpkg && zip -qr9 "$ROOT/dist/WR3000X-win64.zip" WR3000X)
ls -la dist/WR3000X-win64.zip
