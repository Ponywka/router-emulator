#!/bin/bash
# Build the MT7981 Router Emulator: QEMU v10.1.0 + qemu-patches/*.patch
set -e
cd "$(dirname "$(readlink -f "$0")")"
ROOT=$PWD
if [ ! -d src/qemu/.git ]; then
    mkdir -p src
    git clone --depth 1 --branch v10.1.0 https://gitlab.com/qemu-project/qemu.git src/qemu
fi
cd src/qemu
if ! git rev-parse -q --verify mt7981 >/dev/null; then
    git checkout -b mt7981
    git -c user.name=build -c user.email=build@localhost am "$ROOT"/qemu-patches/*.patch
fi
mkdir -p build && cd build
[ -f build.ninja ] || ../configure --target-list=aarch64-softmmu --enable-slirp \
    --enable-fdt=system --disable-docs --disable-werror
ninja
echo "built: $PWD/qemu-system-aarch64"
