# Сборка под Linux

[English](README.build.linux.md) · **Русский** · [Обзор](README.ru.md) · [Сборка под Windows](README.build.windows.ru.md)

Проверено на Debian 13 (x86-64). На других дистрибутивах — аналогичные пакеты.

## 1. Зависимости

```bash
sudo apt-get install -y build-essential git ninja-build meson pkg-config \
    python3 python3-venv libglib2.0-dev libpixman-1-dev libslirp-dev \
    libfdt-dev zlib1g-dev \
    mtd-utils wget u-boot-tools device-tree-compiler \
    bridge-utils iproute2 iptables libpcap0.8t64
```

- `mtd-utils` (`ubinize`), `wget` — сборка образов NAND
  ([`tools/prepare-nand.sh`](tools/prepare-nand.sh), [`tools/mknand.py`](tools/mknand.py)).
- `bridge-utils`, `iproute2`, `iptables` — сеть хоста ([`tools/host-bridge.sh`](tools/host-bridge.sh)).
- `libpcap` — только для необязательного `-netdev pcap` (загружается при запуске).

## 2. Сборка QEMU с машинами WR3000X

```bash
./build.sh
```

Что делает [`build.sh`](build.sh):

1. клонирует QEMU **v10.1.0** в `src/qemu` (shallow);
2. создаёт ветку `wr3000x` и применяет [`qemu-patches/*.patch`](qemu-patches/) через `git am`;
3. конфигурирует `--target-list=aarch64-softmmu --enable-slirp --enable-fdt=system`;
4. собирает `ninja`.

Результат: `src/qemu/build/qemu-system-aarch64`. Проверка:

```bash
src/qemu/build/qemu-system-aarch64 -M help | grep cudy
```

То же вручную:

```bash
git clone --depth 1 --branch v10.1.0 https://gitlab.com/qemu-project/qemu.git src/qemu
cd src/qemu && git checkout -b wr3000x && git am ../../qemu-patches/*.patch
mkdir build && cd build
../configure --target-list=aarch64-softmmu --enable-slirp --enable-fdt=system --disable-docs
ninja
```

После правок в `src/qemu/hw/arm/mt7981/` достаточно запустить `ninja` в
`src/qemu/build`. Обновить серию патчей:
`cd src/qemu && git commit ... && git format-patch -o ../../qemu-patches v10.1.0..wr3000x`.

## 3. Образы флеш-памяти

```bash
tools/prepare-nand.sh wr3000p 25.12.5        # -> nand-wr3000p/
tools/prepare-nand.sh wr3000h 25.12.5        # -> nand-wr3000h/
tools/prepare-nand.sh wr3000s 25.12.5
tools/prepare-nand.sh wbr3000uax 25.12.5
tools/prepare-nand.sh wr3000u 25.12.5        # нужны wr3000u/*mtd0*.bin, *mtd4*.bin (+ sysupgrade.bin)
tools/prepare-nand.sh wr3000p snapshot       # снапшот вместо релиза
```

Заранее положите в `factory/` дампы своего роутера `*Factory*.bin`
(калибровка Wi-Fi) и `*bdinfo*.bin` (MAC-адрес); без них Wi-Fi использует
значения по умолчанию, а MAC генерируется случайно.

## 4. Сеть хоста (необязательно)

```bash
sudo tools/host-bridge.sh setup enp0s3 br0   # сетевая карта в br0 (IP/MAC сохраняются, постоянно)
sudo tools/host-bridge.sh taps br0 $USER     # wr-wan -> br0, wr-lan1..4 -> изолированный br-wrlan
tools/host-bridge.sh status
sudo tools/host-bridge.sh teardown           # откат
```

В виртуальной машине (например, VirtualBox) включите для адаптера режим
promiscuous «Allow All», иначе кадры для MAC-адресов роутера не доходят.
`setup` сохраняет резервную копию `/etc/network/interfaces`.

## 5. Запуск

```bash
./wr3000x.sh                     # WR3000P, WAN в br0, lan1 в br-wrlan
./wr3000x.sh -b cudy-wr3000u -n nand-wr3000u -w user -l none
./wr3000x.sh -h                  # все параметры
```

Консоль — этот терминал (Ctrl-A X — выход, Ctrl-A C — монитор QEMU). Логи:
`logs/console_*.log`. Быстрые автоматические проверки: [`tests/quick.py`](tests/quick.py).
