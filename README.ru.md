# WR3000X — эмулятор роутеров Cudy семейства WR3000X (MediaTek MT7981B)

[English](README.md) · **Русский** · Сборка: [Linux](README.build.linux.ru.md) · [Windows](README.build.windows.ru.md)

QEMU-машина, эмулирующая роутеры **Cudy WR3000P, WR3000H, WR3000S, WR3000U
и WBR3000UAX** на уровне железа. Прошивки запускаются **без изменений**, по
той же цепочке загрузки, что и на настоящем устройстве:

```
BootROM (эмулирован) → BL2 (preloader MediaTek, калибровка DDR) → BL31 (TF-A)
→ U-Boot → OpenWrt (ядро + rootfs из UBI на SPI-NAND)
```

Загружаются и собственный загрузчик OpenWrt (разметка «ubootmod»), и стоковый
загрузчик Cudy (BL2/FIP, снятые с настоящего WR3000U, с NMBM). Всё, что для
этого понадобилось, сделано на стороне эмулятора; прошивки не патчатся.

## Быстрый старт

Linux: [README.build.linux.ru.md](README.build.linux.ru.md), затем

```bash
tools/prepare-nand.sh wr3000p 25.12.5   # официальные образы -> nand-wr3000p/
./wr3000x.sh                            # консоль роутера в этом терминале
```

Windows: распакуйте `dist/WR3000X-win64.zip`, запустите `WR3000X.exe`
(как собрать — [README.build.windows.ru.md](README.build.windows.ru.md)).

## Модели

| Машина QEMU | Роутер | RAM / NAND | WAN | Загрузчик в пакете |
|---|---|---|---|---|
| `cudy-wr3000p` | WR3000P v1 | 512 МБ DDR4 / 128 МБ | 2.5G, RTL8221B на GMAC2 | OpenWrt (ubootmod) |
| `cudy-wr3000h` | WR3000H v1 | 512 МБ (BL2 для DDR3) / 128 МБ | 2.5G, RTL8221B, сброс PHY на MDIO-шине | OpenWrt (ubootmod) |
| `cudy-wr3000s` | WR3000S v1 | 256 МБ DDR3 / 128 МБ | 1G, порт 0 MT7531 | OpenWrt (ubootmod) |
| `cudy-wbr3000uax` | WBR3000UAX v1 | 256 МБ DDR3 / 128 МБ | 1G, порт 0 MT7531 | OpenWrt (ubootmod) |
| `cudy-wr3000u` | WR3000U v1 | 256 МБ DDR3 / 256 МБ | 1G, порт 0 MT7531 | стоковый Cudy (BL2 v2.7, U-Boot 2022.07, NMBM) |

GPIO светодиодов у моделей разные; светодиоды — обычные выходы GPIO
(`gpio-log=on` печатает их изменения).

## Что эмулируется

Все модели устройств — в `hw/arm/mt7981/` дерева QEMU
(патчи: [`qemu-patches/`](qemu-patches/), база QEMU v10.1.0).

| Блок | Модель | Примечания |
|---|---|---|
| CPU, GIC | 2× Cortex-A53 (EL3/EL2), GICv3, таймер 13 МГц | CPU1 запускает BL31 через SPMC в TOPMISC; PSCI — от BL31 |
| BootROM | высокоуровневая эмуляция (`cudy_wr3000x.c`) | разбирает заголовок `SPINAND!` и GFH `FILE_INFO`, грузит BL2 в L2 SRAM, переходит на EL3 |
| Контроллер DRAM | `mt7981_sysctl.c` + сгенерированная таблица статусов | broadcast, ответы RTSWCMD/MRW, jitter meter, lead/lag DQS gating, «глаз» приёма RX: бинарная калибровка DRAM от MediaTek находит настоящие окна, лог BL2 чистый (DDR3 и DDR4) |
| Клоки, питание, прочее | `mt7981_sysctl.c` | разреженный регистровый файл + особые случаи (частотомер, включение CPU, TRNG v2, калибровочные данные eFuse, IPPC, термодатчик ≈45 °C, ID EIP-97) |
| APXGPT | `mt7981_sysctl.c` | 8 таймеров общего назначения (нужны стоковому BL2 Cudy) |
| TOPRGU | `mt7981_toprgu.c` | watchdog с реальным таймаутом, программный сброс (reboot), статус сброса сохраняется |
| UART ×3 | 16550 из QEMU + регистры MTK | |
| SPI (IPM) | `mt7981_spim.c` | FIFO + DMA, полудуплексный режим spi-mem (TF-A/U-Boot/Linux) |
| SPI-NAND | `spinand.c` | W25N01GV (128 МБ) / W25N02KV (256 МБ), on-die ECC, ONFI parameter page; хранилище — папка с дампами разделов (см. ниже) или один raw-образ с OOB |
| Ethernet | `mt7981_eth.c` | frame engine: QDMA TX (Linux), PDMA RX, PDMA v2 (U-Boot); TSO и offload контрольных сумм; 2× LynxI SGMII PCS |
| Коммутатор | `mt7981_eth.c` | MT7531: страничный доступ по MDIO, косвенный доступ к PHY, special tag MTK (DSA), FDB с обучением, port matrix, IRQ линка → EINT 38 |
| PHY | `mt7981_eth.c` | 5× GPHY MT7531, RTL8221B-VB-CG (C45, учитывает аппаратный сброс по GPIO 3), встроенный GbE PHY MT7981 (с калибровкой) |
| GPIO / EINT | `mt7981_pinctrl.c` | кнопки (reset/WPS через QOM), лог светодиодов, уровни выводов для других устройств |
| USB | xHCI из QEMU + IPPC MTK | можно подключать USB-накопители и т.д. |
| Wi-Fi | `mt7981_wmac.c` | кольца WFDMA + эмуляция командного интерфейса прошивок WM/WA: прошивка грузится, оба диапазона поднимаются, hostapd работает, в эфире ничего нет (сканирование пустое) |
| Крипто (EIP-97) | только ID | драйвер safexcel видит «нет packet engine» и отключается; работает программная криптография |

Сеть: любой netdev QEMU; порты выбираются по id: `lan1`..`lan4`, `wan`.
Новый netdev `pcap` (`net/pcap.c`, libpcap / Npcap загружается при запуске)
подключает порт к сетевой карте хоста как «сетевой мост» — им пользуется
Windows-лаунчер.

## Флеш (папка NAND)

Папка NAND содержит дампы разделов без OOB. **Каждый файл, в имени которого
есть `mtdN`, становится разделом N**; файлы склеиваются по порядку mtd0, mtd1,
… в полный образ, например:

```
cudy_wr3000x.mtd0.BL2.bin   cudy_wr3000x.mtd1.u-boot-env.bin
cudy_wr3000x.mtd2.Factory.bin   cudy_wr3000x.mtd3.bdinfo.bin
cudy_wr3000x.mtd4.FIP.bin   cudy_wr3000x.mtd5.ubi.bin
```

Всё, что роутер пишет во флеш (настройки, sysupgrade, env U-Boot),
записывается обратно в эти файлы. Дампы с настоящего роутера
(`cat /dev/mtdN > имя.mtdN.метка.bin`) подходят напрямую.

- [`tools/prepare-nand.sh`](tools/prepare-nand.sh) `МОДЕЛЬ [ВЕРСИЯ] [ПАПКА]` — скачивает официальные образы OpenWrt (с проверкой sha256) и собирает `nand-МОДЕЛЬ/`; Factory/bdinfo берутся из `factory/`. Для `wr3000u` используются стоковые BL2/FIP Cudy из `wr3000u/` и `sysupgrade.bin` в стоковой разметке.
- [`tools/mknand.py`](tools/mknand.py) — создание и правка образов: `create` (BL2, FIP, Factory, bdinfo, UBI из `.itb` или стокового `sysupgrade.bin`), `write --part fip`, `read`, `split`, `join`, `--flash-mb 256`.

## Запуск

Linux: [`wr3000x.sh`](wr3000x.sh) — `-b МОДЕЛЬ`, `-n ПАПКА_NAND`,
`-w bridge|user|none`, `-l isolated|nic|none`, `-p "1 3"` (порты LAN),
`-u ПАПКА` (USB-флешка из папки, FAT16), `-L ПАПКА` (логи консоли),
`-g` (лог GPIO), `-d` (лог неэмулированных регистров).
Сеть хоста: [`tools/host-bridge.sh`](tools/host-bridge.sh) (`br0` с сетевой
картой для WAN, изолированный `br-wrlan` для LAN — LAN в реальной сети
выставил бы туда DHCP/RA роутера).

Windows: `WR3000X.exe` — модель, папка NAND, WAN/LAN (NAT, «только этот ПК»
с пробросом портов на LuCI/SSH или мост на сетевую карту через Npcap),
USB-папка, папка логов, кнопки Reset/WPS, выключение по `poweroff`,
встроенный терминал (VT100, выделение = копирование, правая кнопка = вставка,
Ctrl+Shift+R подгоняет размер консоли роутера под окно).

## Структура репозитория

```
wr3000x.sh                запуск под Linux
build.sh                  сборка QEMU (Linux)         → README.build.linux.ru.md
build-windows.sh          пакет для Windows (кросс)   → README.build.windows.ru.md
qemu-patches/             патчи поверх QEMU v10.1.0
tools/                    mknand.py, prepare-nand.sh, host-bridge.sh,
                          gen_dramc_table.py (статусы DRAMC по умолчанию)
windows/                  лаунчер и терминал (C#), README.txt для пакета
tests/                    quick.py (проверки по консоли), старые pexpect-скрипты
factory/                  дампы Factory (EEPROM Wi-Fi) и bdinfo (MAC)
wr3000u/                  стоковые BL2/FIP Cudy + sysupgrade.bin для WR3000U
```

## Заметки для разработчиков

- Исходники, по которым моделировалось железо: дерево Linux 6.18 из OpenWrt
  (vanilla + патчи OpenWrt), U-Boot, TF-A от mtk-openwrt, mt76, код DRAMC
  MediaTek из coreboot (MT8192/MT8195, то же поколение DRAMC).
- `-d unimp` логирует каждое обращение к регистрам без явной модели —
  самый быстрый способ понять, что опрашивает новая прошивка.
- [`tests/quick.py`](tests/quick.py) запускает QEMU с консолью на сокете и
  ждёт строки в консоли с жёсткими лимитами времени, например
  `tests/quick.py --qemu "-netdev user,id=wan" 'Starting kernel@60' 'wan: Link is Up@120'`.
- Windows-сборка собирается clang (нативный TLS): эмулируемый TLS в MinGW GCC
  замедлял выполнение гостя примерно на 30 %.

## Ограничения

- Радио Wi-Fi «молчит» (клиентов нет, сканирование пустое); командный
  интерфейс MCU отвечает обобщённо.
- Нет packet engine EIP-97 и PCIe-устройств; PWM/I2C — заглушки.
- Альтернативный WAN PHY WR3000H (Motorcomm YT8821) не эмулирован.
- Скорость: примерно в 2 раза медленнее настоящего SoC 1,3 ГГц на обычном ПК (TCG).
