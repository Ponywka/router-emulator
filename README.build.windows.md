# Building the Windows package

**English** · [Русский](README.build.windows.ru.md) · [Overview](README.md) · [Linux build](README.build.linux.md)

The Windows package is **cross-built on Linux**: QEMU with MinGW libraries
in a Docker container, the launcher with the Mono C# compiler. Output:
`dist/WR3000X-win64.zip` — unpack anywhere on Windows 10/11 x64 and run
`WR3000X.exe` (.NET Framework 4.8 is part of Windows). Npcap
(<https://npcap.com>) is needed only to bridge router ports to a network
adapter.

## 1. Requirements (Linux host)

```bash
sudo apt-get install -y docker.io mono-mcs mono-devel zip mtd-utils wget python3 git
```

- Docker: QEMU's own Fedora MinGW cross image
  (`src/qemu/tests/docker/dockerfiles/fedora-win64-cross.docker`) plus clang/lld.
- `mono-mcs` / `mono-devel`: C# compiler and the **.NET Framework 4.8
  reference assemblies** (`/usr/lib/mono/4.8-api`).
- The QEMU source tree from the [Linux build](README.build.linux.md)
  (`./build.sh`; `build-windows.sh` runs it if `src/qemu` is missing).

## 2. Build

```bash
./build-windows.sh                    # OpenWrt 25.12.5 NAND images
VERSION=snapshot ./build-windows.sh   # other OpenWrt version
```

What [`build-windows.sh`](build-windows.sh) does:

1. builds Docker images `qemu-win64-cross` (QEMU's Dockerfile) and
   `qemu-win64-clang` (+ clang, lld);
2. configures QEMU in `src/qemu/build-win-clang` with
   `clang --target=x86_64-w64-windows-gnu --sysroot=<Fedora MinGW sysroot> -fuse-ld=lld`,
   `--extra-ldflags=-L<libgcc dir>`, `--enable-slirp --enable-fdt=internal`,
   without GTK/SDL/VNC/OpenGL/curl/tools; runs `ninja`;
3. copies `qemu-system-aarch64.exe`, `libslirp-0.dll` and every MinGW DLL it
   depends on (recursive `objdump -p`), then `strip --strip-all` on all of them;
4. compiles the launcher (`windows/WR3000X.cs`, `windows/Terminal.cs`)
   **against the .NET Framework 4.8 reference assemblies** — Mono's own
   libraries contain newer APIs that would fail on Windows with
   `MissingMethodException`;
5. builds the NAND folders with [`tools/prepare-nand.sh`](tools/prepare-nand.sh)
   (WR3000P/H/S, WBR3000UAX; WR3000U if `wr3000u/` contains the stock dumps);
6. zips everything into `dist/WR3000X-win64.zip`.

### Why clang and not MinGW GCC

MinGW GCC implements thread-local variables with emulated TLS
(`__emutls_get_address`, ~5000 call sites in QEMU). clang targeting
`x86_64-w64-windows-gnu` uses native TLS; guest code runs ~30 % faster.
The same Fedora MinGW libraries are used, only QEMU itself is compiled by
clang. Plain GCC still works: configure with
`--cross-prefix=x86_64-w64-mingw32-` in the `qemu-win64-cross` image.

## 3. Package layout

```
WR3000X/
  WR3000X.exe          launcher + serial terminal
  qemu/                qemu-system-aarch64.exe + DLLs
  nand-wr3000p/ ...    flash folders (one per board)
  usb/                 exported to the router as a USB stick
  logs/                console logs (console_YYYY-MM-DD_HH-mm-ss.log)
  README.txt           user manual (windows/README.txt)
```

## 4. Testing without Windows

Wine runs the package for smoke tests (Wine needs `wine-mono` for the
launcher):

```bash
WINEPREFIX=$PWD/work/wineprefix tests/quick.py --win \
  -M 'cudy-wr3000p,nand-dir=Z:\path\to\nand-wr3000p' \
  --qemu "-netdev user,id=wan" 'Starting kernel@60' 'wan: Link is Up@150'
```

Limits: Wine's built-in wpcap lacks `pcap_getevent`, so the Npcap bridge can
only be tested on real Windows; Wine runs the launcher on Mono, not on .NET
Framework.

## 5. Building natively on Windows (not tested)

- Launcher: the C# compiler that ships with .NET Framework is enough (code is
  C# 5 compatible):
  `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:WR3000X.exe WR3000X.cs Terminal.cs`
- QEMU: MSYS2 CLANG64 environment with the usual QEMU dependencies, then the
  same `configure` options as above (without `--cross-prefix`).
