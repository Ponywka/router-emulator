# WR3000X — эмулятор роутеров Cudy WR3000X (MediaTek MT7981B)

Эмулирует Cudy **WR3000P v1**, **WR3000S v1**, **WR3000U v1** и **WBR3000UAX v1**
на уровне железа: QEMU-машина с моделью SoC MT7981B. Прошивки запускаются
**без изменений**, по той же цепочке, что и на реальном роутере:

```
BootROM (эмулирован) → BL2 (preloader, калибровка DDR) → BL31 (TF-A)
→ U-Boot → OpenWrt (ядро + rootfs из UBI на SPI-NAND)
```

## Быстрый старт (Linux)

```bash
./build.sh                                   # QEMU 10.1.0 + патчи из qemu-patches/
tools/prepare-nand.sh wr3000p 25.12.5        # официальные образы → папка nand-wr3000p/
./wr3000x.sh                                 # консоль роутера в этом терминале
```

Выход из QEMU: `Ctrl-A X`. Монитор QEMU: `socat - UNIX-CONNECT:work/monitor.sock`.

Опции `wr3000x.sh` (`./wr3000x.sh -h`):

| опция | значение |
|---|---|
| `-b BOARD` | `cudy-wr3000p` (по умолчанию), `cudy-wr3000s`, `cudy-wr3000u`, `cudy-wbr3000uax` |
| `-n DIR` | папка NAND (по умолчанию `nand-wr3000p/`) |
| `-w bridge\|user\|none` | WAN: tap `wr-wan` в `br0` (сетевая карта), NAT QEMU или не подключён |
| `-l isolated\|nic\|none` | LAN: изолированный мост `br-wrlan` (хост = 192.168.1.2), мост с сетевой картой или нет |
| `-p "1 3"` | какие LAN-порты подключить (по умолчанию только lan1, иначе петля) |
| `-u DIR\|none` | папка, видимая роутеру как USB-флешка (по умолчанию `usb/`) |
| `-g` | печатать изменения GPIO (светодиоды) |
| `-d` | лог неэмулированных регистров в `work/qemu.log` |

## Windows

`./build-windows.sh` собирает `dist/WR3000X-win64.zip`: QEMU для Windows,
графический лаунчер `WR3000X.exe` (выбор модели, папки NAND, сетевой карты
для WAN/LAN, USB-папки, кнопки Reset/WPS) и чистые NAND с OpenWrt 25.12.5.
Подключение портов роутера к сетевой карте работает через Npcap
(https://npcap.com), как «сетевой мост» в VirtualBox.
Подробности — в `windows/README.txt`.

## NAND

Папка NAND — это дампы разделов (без OOB): каждый файл, в имени которого есть
`mtdN`, становится разделом N, файлы склеиваются по порядку mtd0, mtd1, …
в полный образ флеш-памяти. Например:

```
cudy_wr3000x.mtd0.BL2.bin  cudy_wr3000x.mtd1.u-boot-env.bin
cudy_wr3000x.mtd2.Factory.bin  cudy_wr3000x.mtd3.bdinfo.bin
cudy_wr3000x.mtd4.FIP.bin  cudy_wr3000x.mtd5.ubi.bin
```

Всё, что роутер пишет во флеш (настройки, sysupgrade, переменные U-Boot),
сохраняется обратно в эти файлы. Подходят и дампы с настоящего роутера
(`cat /dev/mtdN > имя.mtdN.метка.bin`).

`tools/mknand.py` — создание/правка образов: `create`, `write --part fip`,
`read`, `split` (полный дамп → папка), `join` (папка → raw-образ с OOB).
Factory (калибровка Wi-Fi) и bdinfo (MAC-адрес) берутся из `factory/`.

## Что эмулируется

| блок | состояние |
|---|---|
| 2× Cortex-A53, GICv3, generic timer 13 МГц, PSCI через BL31 | полностью |
| BootROM (SPI-NAND, BROM header + GFH) | эмулирован |
| DRAMC/DDRPHY (калибровка DDR3/DDR4 бинарным blob'ом MediaTek) | проходит; blob печатает предупреждения о «не найденных окнах» (косметика) |
| PLL/частотомер, TOPRGU (watchdog, reboot, причина сброса) | да |
| SPI IPM + SPI-NAND W25N01GV / W25N02KV (ONFI parameter page) | да |
| UART ×3, GPIO + EINT, кнопки Reset/WPS, светодиоды | да |
| Ethernet: QDMA/PDMA, TSO/checksum offload, LynxI SGMII | да |
| MT7531 (DSA, special tag, FDB, port matrix, IRQ) + 5 GPHY | да |
| RTL8221B-VB-CG 2.5G (WAN WR3000P) | да |
| USB 3.0 xHCI + MediaTek IPPC | да (usb-storage и т.п.) |
| Wi-Fi MT7981 (WFDMA + «прошивка» WM/WA) | радио «молчит»: AP поднимается, сканирование пустое |
| TRNG | да |
| EIP-97 crypto | нет (драйвер корректно отключается, используется программная криптография) |
| PCIe, thermal, PWM, I2C | заглушки |

## Структура

```
wr3000x.sh            запуск (Linux)
build.sh              сборка QEMU (Linux)
build-windows.sh      сборка пакета для Windows
qemu-patches/         патчи к QEMU v10.1.0 (hw/arm/mt7981/*, net/pcap.c)
tools/mknand.py       работа с образами NAND
tools/prepare-nand.sh скачать официальные образы OpenWrt и собрать NAND
tools/host-bridge.sh  мост br0 с сетевой картой, tap-интерфейсы
tools/gen_dramc_table.py  таблица статусов DRAMC из регистров MediaTek
windows/              лаунчер для Windows (C#) и README
tests/                скрипты автотестов (pexpect)
```
