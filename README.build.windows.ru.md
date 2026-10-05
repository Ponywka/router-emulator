# Сборка пакета для Windows

[English](README.build.windows.md) · **Русский** · [Обзор](README.ru.md) · [Сборка под Linux](README.build.linux.ru.md)

Пакет для Windows **собирается на Linux кросс-компиляцией**: QEMU — с
библиотеками MinGW в Docker-контейнере, лаунчер — компилятором C# из Mono.
Результат: `dist/WR3000X-win64.zip` — распаковать в любое место на
Windows 10/11 x64 и запустить `WR3000X.exe` (.NET Framework 4.8 входит в
Windows). Npcap (<https://npcap.com>) нужен только для моста портов роутера на
сетевую карту.

## 1. Требования (хост с Linux)

```bash
sudo apt-get install -y docker.io mono-mcs mono-devel zip mtd-utils wget python3 git
```

- Docker: собственный образ QEMU для кросс-сборки Fedora MinGW
  (`src/qemu/tests/docker/dockerfiles/fedora-win64-cross.docker`) плюс clang/lld.
- `mono-mcs` / `mono-devel`: компилятор C# и **эталонные сборки .NET
  Framework 4.8** (`/usr/lib/mono/4.8-api`).
- Дерево исходников QEMU из [сборки под Linux](README.build.linux.ru.md)
  (`./build.sh`; `build-windows.sh` запускает его сам, если `src/qemu` нет).

## 2. Сборка

```bash
./build-windows.sh                    # образы NAND с OpenWrt 25.12.5
VERSION=snapshot ./build-windows.sh   # другая версия OpenWrt
```

Что делает [`build-windows.sh`](build-windows.sh):

1. собирает Docker-образы `qemu-win64-cross` (Dockerfile из QEMU) и
   `qemu-win64-clang` (+ clang, lld);
2. конфигурирует QEMU в `src/qemu/build-win-clang` с
   `clang --target=x86_64-w64-windows-gnu --sysroot=<sysroot Fedora MinGW> -fuse-ld=lld`,
   `--extra-ldflags=-L<каталог libgcc>`, `--enable-slirp --enable-fdt=internal`,
   без GTK/SDL/VNC/OpenGL/curl/tools; запускает `ninja`;
3. копирует `qemu-system-aarch64.exe`, `libslirp-0.dll` и все DLL MinGW, от
   которых они зависят (рекурсивно по `objdump -p`), и делает всем
   `strip --strip-all`;
4. компилирует лаунчер (`windows/WR3000X.cs`, `windows/Terminal.cs`)
   **против эталонных сборок .NET Framework 4.8** — в библиотеках Mono есть
   более новые методы, которые на Windows дали бы `MissingMethodException`;
5. собирает папки NAND через [`tools/prepare-nand.sh`](tools/prepare-nand.sh)
   (WR3000P/H/S, WBR3000UAX; WR3000U — если в `wr3000u/` лежат стоковые дампы);
6. упаковывает всё в `dist/WR3000X-win64.zip`.

### Почему clang, а не MinGW GCC

MinGW GCC реализует потоко-локальные переменные эмулируемым TLS
(`__emutls_get_address`, около 5000 мест вызова в QEMU). clang с целью
`x86_64-w64-windows-gnu` использует нативный TLS; код гостя выполняется
примерно на 30 % быстрее. Библиотеки те же, из Fedora MinGW, clang'ом
компилируется только сам QEMU. Обычный GCC тоже работает: configure с
`--cross-prefix=x86_64-w64-mingw32-` в образе `qemu-win64-cross`.

## 3. Состав пакета

```
WR3000X/
  WR3000X.exe          лаунчер + терминал последовательного порта
  qemu/                qemu-system-aarch64.exe + DLL
  nand-wr3000p/ ...    папки флеш-памяти (по одной на модель)
  usb/                 отдаётся роутеру как USB-флешка
  logs/                логи консоли (console_ГГГГ-ММ-ДД_ЧЧ-мм-сс.log)
  README.txt           руководство пользователя (windows/README.txt)
```

## 4. Проверка без Windows

Пакет можно запускать под Wine для быстрых проверок (лаунчеру нужен
`wine-mono`):

```bash
WINEPREFIX=$PWD/work/wineprefix tests/quick.py --win \
  -M 'cudy-wr3000p,nand-dir=Z:\path\to\nand-wr3000p' \
  --qemu "-netdev user,id=wan" 'Starting kernel@60' 'wan: Link is Up@150'
```

Ограничения: во встроенном wpcap Wine нет `pcap_getevent`, поэтому мост через
Npcap проверяется только на настоящей Windows; лаунчер под Wine работает на
Mono, а не на .NET Framework.

## 5. Сборка прямо на Windows (не проверялась)

- Лаунчер: достаточно компилятора C#, входящего в .NET Framework (код
  совместим с C# 5):
  `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:WR3000X.exe WR3000X.cs Terminal.cs`
- QEMU: окружение MSYS2 CLANG64 с обычными зависимостями QEMU, затем те же
  параметры `configure`, что выше (без `--cross-prefix`).
