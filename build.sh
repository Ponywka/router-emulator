#!/bin/bash
# Build the WR3000X emulator: QEMU v10.1.0 + qemu-patches/*.patch
set -e
cd "$(dirname "$(readlink -f "$0")")"
ROOT=$PWD
if [ ! -d src/qemu/.git ]; then
    mkdir -p src
    git clone --depth 1 --branch v10.1.0 https://gitlab.com/qemu-project/qemu.git src/qemu
fi
cd src/qemu
if ! git rev-parse -q --verify wr3000x >/dev/null; then
    git checkout -b wr3000x
    git -c user.name=build -c user.email=build@localhost am "$ROOT"/qemu-patches/*.patch
fi
mkdir -p build && cd build
[ -f build.ninja ] || ../configure --target-list=aarch64-softmmu --enable-slirp \
    --enable-fdt=system --disable-docs --disable-werror
ninja
echo "built: $PWD/qemu-system-aarch64"
