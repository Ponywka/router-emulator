# MT7981 Router Emulator (MediaTek MT7981B / Filogic 820)

Версия **0.1** ([`VERSION`](VERSION)) · [English](README.md) · **Русский** · Сборка: [Linux](README.build.linux.ru.md) · [Windows](README.build.windows.ru.md)

QEMU-машина `mt7981-router`, эмулирующая плату роутера на MT7981B на
уровне железа. Железо платы (PHY/коммутатор Ethernet, флеш, тип и размер
RAM, USB) настраивается, поэтому одна машина покрывает много устройств;
конкретные роутеры описываются **пресетами** (`presets/*.ini`). Прошивки
запускаются **без изменений**, по той же цепочке загрузки, что и на
настоящем устройстве:

```
BootROM (эмулирован) → BL2 (preloader MediaTek, калибровка DDR) → BL31 (TF-A)
→ U-Boot → OpenWrt (ядро + rootfs из UBI на SPI-NAND)
```

Загружаются и собственный загрузчик OpenWrt (разметка «ubootmod»), и
стоковые загрузчики производителя (BL2/FIP, снятые с настоящих устройств,
с NMBM). Всё, что для этого понадобилось, сделано на стороне эмулятора;
прошивки не патчатся.

## Быстрый старт

Linux: [README.build.linux.ru.md](README.build.linux.ru.md), затем

```bash
tools/prepare-nand.sh cudy_wr3000p-v1 25.12.5   # официальные образы -> nand-wr3000p/
./mt7981.sh -P list                             # пресеты плат
./mt7981.sh -P cudy-wr3000p-v1                  # консоль роутера в этом терминале
```

Windows: скачайте zip из релиза (или соберите сами —
[README.build.windows.ru.md](README.build.windows.ru.md)), распакуйте,
запустите `MT7981.exe`. Папок NAND в релизе нет: соберите их
`tools/prepare-nand.sh` (Linux или WSL) и положите рядом с `MT7981.exe`
или используйте дампы с настоящего роутера.

## Железо платы (параметры машины)

`-M mt7981-router,nand-dir=ПАПКА,<параметры>` и `-m <размер RAM>`:

| Параметр | Значения | Смысл |
|---|---|---|
| `gmac0` | `mt7531` · `rtl8221b` · `yt8821` · `none` | что подключено к GMAC0 (mac@0, SGMII0) |
| `ports` | например `wan:lan1:lan2:lan3:lan4` | id netdev портов 0..4 MT7531 (`-` — не используется) |
| `gmac0-port`, `gmac1-port` | id netdev | порт PHY, подключённого прямо к GMAC |
| `gmac1` | `rtl8221b` · `yt8821` · `gphy` · `none` | GMAC1 (mac@1); `gphy` — встроенный 1G PHY MT7981 |
| `gmac0-reset-gpio`, `gmac1-reset-gpio` | GPIO, `-1` (по умолчанию) | линия аппаратного сброса 2.5G PHY; по умолчанию не подключена (отсутствие линии безвредно, неверная могла бы держать PHY в сбросе) |
| `nand` | `128` · `256` | SPI-NAND W25N01GV / W25N02KV |
| `ddr` | `ddr3` · `ddr4` | тип распаянной памяти: BL2, собранный для другого типа, останавливает машину, как не проходит инициализация DRAM на настоящей плате |
| `usb-port` | `none` · `2` · `3` | разъём USB (USB 3.0: устройства подключаются на SuperSpeed) |
| `reset-gpio`, `wps-gpio` | GPIO | кнопки (QOM `/machine/pinctrl` `reset-button`, `wps-button`) |
| `reset-active-high`, `wps-active-high` | `on` | кнопка читается как 1 при нажатии (по умолчанию active low) |
| `reset-hold` | мс | включение с зажатым reset (TFTP recovery в U-Boot) |
| `gpio-log` | `on` | печать изменений выходов GPIO (светодиоды) |

Сетевые порты — netdev QEMU с id из таблицы (`wan`, `lan1`, …).
Ключи пресета только для лаунчеров: `lan-ip` (адрес роутера в LAN, по
умолчанию 192.168.1.1) и `lan-forwards` (по умолчанию
`8080:80,8443:443,8022:22`, порт ПК:порт роутера) для LAN1 «только этот
ПК»: сеть QEMU user-mode в /24 роутера с `restrict=on` и этими пробросами
со 127.0.0.1.

## Пресеты плат

Пресет — INI-файл: `name`, `description`, `ram` (МБ), `nand-dir`, ключи
только для сборки пакета (`openwrt=` профиль OpenWrt, `openwrt-local=`
своя сборка, `openwrt-stock=` дампы стокового загрузчика) и параметры
машины. Их используют оба лаунчера; в Windows-лаунчере есть редактор
(Ethernet, подписи портов коммутатора, тип/размер RAM, NAND, USB, GPIO
кнопок с галкой «active high» — снята = active low, это умолчание и так
у большинства роутеров; при сохранении из редактора комментарии `;`
пропадают).

| Пресет | Ethernet | RAM | NAND | USB | Загрузчик |
|---|---|---|---|---|---|
| Cudy WR3000P v1 | 2.5G WAN RTL8221B + 4×1G MT7531 | DDR4 512 МБ | 128 МБ | 2.0 | OpenWrt |
| Cudy WR3000H v1 | 2.5G WAN RTL8221B + 4×1G MT7531 | DDR3 512 МБ | 128 МБ | – | OpenWrt |
| Cudy WR3000S v1, WR3000E v1 | 5×1G MT7531 (WAN = порт 0) | DDR3 256 МБ | 128 МБ | – | OpenWrt |
| Cudy WBR3000UAX v1 | 5×1G MT7531 (WAN = порт 0) | DDR3 256 МБ | 128 МБ | 3.0 | OpenWrt |
| Cudy WR3000U v1 | 5×1G MT7531 (WAN = порт 0) | DDR3 256 МБ | 256 МБ | – | стоковый (NMBM) |
| Cudy TR3000 v1 | 2.5G WAN RTL8221B на GMAC0 + 1G LAN встроенный PHY | DDR3 512 МБ | 128 МБ | 3.0 | OpenWrt |
| Cudy TR3000 256MB v1 | то же, без коммутатора | DDR3 512 МБ | 256 МБ | 3.0 | стоковый (NMBM) |
| Cudy M3000 v1 / v2 (RTL8221B) | 2.5G WAN RTL8221B + 1G LAN встроенный PHY | DDR3 256 МБ | 128 МБ | – | OpenWrt (своя сборка) |
| Cudy M3000 v2 (YT8821) | 2.5G WAN Motorcomm YT8821 + 1G LAN встроенный PHY | DDR3 256 МБ | 128 МБ | – | OpenWrt (своя сборка) |
| Netis NX30 V2, NX31 | 1G WAN встроенный PHY + 3×1G LAN MT7531 | DDR3 256 МБ | 128 МБ | – | OpenWrt |
| Netis NX32U | 4×1G MT7531 (WAN = порт 0) | DDR3 256 МБ | 128 МБ | 3.0 | OpenWrt |

У плат Netis нет раздела bdinfo (FIP с 0x380000, ubi с 0x580000; MAC — в
Factory): в пресетах стоит `openwrt-no-bdinfo=1`.

## Что эмулируется

Все модели устройств — в `hw/arm/mt7981/` дерева QEMU
(патчи: [`qemu-patches/`](qemu-patches/), база QEMU v10.1.0).

| Блок | Модель | Примечания |
|---|---|---|
| CPU, GIC | 2× Cortex-A53 (EL3/EL2), GICv3, таймер 13 МГц | CPU1 запускает BL31 через SPMC в TOPMISC; PSCI — от BL31 |
| BootROM | высокоуровневая эмуляция (`mt7981_router.c`) | разбирает заголовок `SPINAND!` и GFH `FILE_INFO`, грузит BL2 в L2 SRAM, переходит на EL3 |
| Контроллер DRAM | `mt7981_sysctl.c` + сгенерированная таблица статусов | broadcast, ответы RTSWCMD/MRW, jitter meter, lead/lag DQS gating, «глаз» приёма RX: бинарная калибровка DRAM от MediaTek находит настоящие окна, лог BL2 чистый (DDR3 и DDR4); биты DDR3EN/DDR4EN регистра `DDRCOMMON0` сверяются с `ddr=` |
| Клоки, питание, прочее | `mt7981_sysctl.c` | разреженный регистровый файл + особые случаи (частотомер, включение CPU, TRNG v2, калибровочные данные eFuse, IPPC, термодатчик ≈45 °C, ID EIP-97, биты сброса WED) |
| APXGPT | `mt7981_sysctl.c` | 8 таймеров общего назначения (нужны стоковым BL2) |
| TOPRGU | `mt7981_toprgu.c` | watchdog с реальным таймаутом, программный сброс (reboot), статус сброса сохраняется |
| UART ×3 | 16550 из QEMU + регистры MTK | |
| SPI (IPM) | `mt7981_spim.c` | FIFO + DMA, полудуплексный режим spi-mem (TF-A/U-Boot/Linux) |
| SPI-NAND | `spinand.c` | W25N01GV (2048+64) / W25N02KV (2048+128), on-die ECC, ONFI parameter page; хранилище — папка с дампами разделов (см. ниже) или один raw-образ с OOB |
| Ethernet | `mt7981_eth.c` | frame engine: QDMA TX (Linux), PDMA RX, PDMA v2 (U-Boot); TSO и offload контрольных сумм; 2× LynxI SGMII PCS; любая комбинация коммутатора и PHY на двух GMAC |
| Коммутатор | `mt7981_eth.c` | MT7531: страничный доступ по MDIO, косвенный доступ к PHY, special tag MTK (DSA), FDB с обучением, port matrix, IRQ линка → EINT 38 |
| PHY | `mt7981_eth.c` | 5× GPHY MT7531; RTL8221B-VB-CG (C45, термодатчик); Motorcomm YT8821 (расширенные регистры, пространства UTP/SerDes, статус 2.5G); встроенный GbE PHY MT7981 (с калибровкой); необязательные GPIO аппаратного сброса |
| GPIO / EINT | `mt7981_pinctrl.c` | кнопки (reset/WPS, `reset-hold-ms` для нажатия на время), лог светодиодов, уровни выводов для других устройств |
| USB | xHCI из QEMU + IPPC MTK | разъём USB 2.0 / 3.0 |
| Wi-Fi | `mt7981_wmac.c` | кольца WFDMA + эмуляция командного интерфейса прошивок WM/WA: прошивка грузится, оба диапазона поднимаются, hostapd работает, в эфире ничего нет (сканирование пустое) |
| Крипто (EIP-97) | только ID | драйвер safexcel видит «нет packet engine» и отключается; работает программная криптография |
| WED | только биты сброса | с `wed_enable=1` mt7915e видит, что WO MCU нет, и работает без offload |

Новый netdev `pcap` (`net/pcap.c`, libpcap / Npcap загружается при запуске)
подключает порт к сетевой карте хоста как «сетевой мост» — им пользуется
Windows-лаунчер.

## Флеш (папка NAND)

Папка NAND содержит дампы разделов без OOB. **Каждый файл, в имени которого
есть `mtdN`, становится разделом N**; файлы склеиваются по порядку mtd0,
mtd1, … в полный образ, например `mt7981.mtd0.BL2.bin`,
`mt7981.mtd1.u-boot-env.bin`, `mt7981.mtd2.Factory.bin`,
`mt7981.mtd3.bdinfo.bin`, `mt7981.mtd4.FIP.bin`, `mt7981.mtd5.ubi.bin`.

Всё, что роутер пишет во флеш (настройки, sysupgrade, env U-Boot),
записывается обратно в эти файлы. Дампы с настоящего роутера
(`cat /dev/mtdN > имя.mtdN.метка.bin`) подходят напрямую.

- [`tools/prepare-nand.sh`](tools/prepare-nand.sh) `[--stock ПАПКА | --local ПАПКА] [--flash-mb N] [--no-bdinfo] ПРОФИЛЬ [ВЕРСИЯ] [ПАПКА_NAND]` — собирает папку NAND для профиля устройства OpenWrt: скачивает официальные образы с U-Boot OpenWrt (`ПРОФИЛЬ-ubootmod-*` или `ПРОФИЛЬ-*`, с проверкой sha256), или берёт свои сборки (`--local`), или оставляет стоковые BL2/FIP (`--stock`) с OpenWrt `sysupgrade.bin` в стоковой разметке; `--no-bdinfo` — для плат без раздела bdinfo. Factory/bdinfo берутся из `factory/`.
- [`tools/mknand.py`](tools/mknand.py) — создание и правка образов: `create` (BL2, FIP, Factory, bdinfo, UBI из `.itb` или `sysupgrade.bin`), `write --part fip`, `read`, `split`, `join`, `--flash-mb 256`, `--no-bdinfo`.

## Запуск

Linux: [`mt7981.sh`](mt7981.sh) — `-P ПРЕСЕТ` (`-P list`), `-o ПАРАМЕТРЫ`
(переопределить параметры машины, `ram=`), `-n ПАПКА_NAND`,
`-w bridge|user|none`, `-l isolated|nic|user|none` (`user` — только этот
ПК, пробросы из пресета), `-p "1 3"` (порты LAN),
`-u ПАПКА` (USB-флешка из папки, FAT16), `-L ПАПКА` (логи консоли),
`-g` (лог GPIO), `-R` (включение с зажатым reset на 10 с → TFTP recovery),
`-d` (лог неэмулированных регистров).
Сеть хоста: [`tools/host-bridge.sh`](tools/host-bridge.sh) (`br0` с сетевой
картой для WAN, изолированный `br-wrlan` для LAN — LAN в реальной сети
выставил бы туда DHCP/RA роутера).

Windows: `MT7981.exe` — пресет платы (с редактором: New / Edit / Save /
Save as / Delete), папка NAND, WAN/LAN (NAT, «только этот ПК» с пробросом
портов на LuCI/SSH или мост на сетевую карту через Npcap), USB-папка,
папка логов, кнопки Reset/WPS, «Power + Reset: 10 s» (TFTP recovery),
выключение по `poweroff`, встроенный терминал (VT100, по умолчанию 80×24
как в PuTTY, размер прилипает к целым символам, выделение = копирование,
правая кнопка = вставка, Ctrl+Shift+R подгоняет размер консоли роутера
под окно), язык интерфейса переключается на лету
([`languages/*.ini`](languages/): English, Русский; новый язык — копия
`en.ini`; [`tools/gen-lang-en.py`](tools/gen-lang-en.py) пересобирает
`en.ini` из исходников, `--check` показывает непереведённые ключи).

## Структура репозитория

```
mt7981.sh                 запуск под Linux
presets/                  пресеты плат (*.ini)
build.sh                  сборка QEMU (Linux)         → README.build.linux.ru.md
build-windows.sh          пакет для Windows (кросс)   → README.build.windows.ru.md
qemu-patches/             патчи поверх QEMU v10.1.0
tools/                    prepare-nand.sh, mknand.py, host-bridge.sh,
                          gen_dramc_table.py (статусы DRAMC по умолчанию)
windows/                  лаунчер, редактор пресетов, терминал (C#),
                          README.txt для пакета
languages/                тексты лаунчера (en.ini, ru.ini)
tests/                    quick.py (проверки по консоли), powercut.py
factory/                  дампы Factory (EEPROM Wi-Fi) и bdinfo (MAC)
wr3000u/, tr3000/         дампы стоковых BL2/FIP (не в git)
m3000/                    свои сборки OpenWrt (не в git)
```

## Заметки для разработчиков

- Исходники, по которым моделировалось железо: дерево Linux из OpenWrt
  (vanilla + патчи OpenWrt), U-Boot, TF-A от mtk-openwrt, mt76, код DRAMC
  MediaTek из coreboot (MT8192/MT8195, то же поколение DRAMC).
- `-d unimp` логирует каждое обращение к регистрам без явной модели —
  самый быстрый способ понять, что опрашивает новая прошивка.
- [`tests/quick.py`](tests/quick.py) запускает QEMU с консолью на сокете и
  ждёт строки в консоли с жёсткими лимитами времени, например
  `tests/quick.py -P cudy-tr3000-v1 --qemu="-netdev user,id=wan" 'Starting kernel@60' 'eth0: Link is Up@120'`.
  [`tests/powercut.py`](tests/powercut.py) выключает/сбрасывает QEMU в
  случайные моменты и проверяет, что BL2 по-прежнему грузит FIP.
- Windows-сборка собирается clang (нативный TLS): эмулируемый TLS в MinGW GCC
  замедлял выполнение гостя примерно на 30 %; кроме того, она оптимизирована
  по профилю (PGO, 5–17 % на настоящей Windows), а лаунчер выводит QEMU из
  режима эффективности Windows 11. По профилю (`perf`) основное время уходит
  на поиск блоков трансляции в TCG, модели устройств почти не видны; -O3/LTO
  ничего не дали. QEMU уже выполняет каждое из двух ядер гостя в своём
  потоке хоста (MTTCG); один поток гостя на несколько ядер не разложить.
- OpenWrt помечает overlay «готовым» только в конце загрузки; перезагрузка
  раньше заставляет fstools его стереть (как и на железе).

## Ограничения

- Радио Wi-Fi «молчит» (клиентов нет, сканирование пустое); командный
  интерфейс MCU отвечает обобщённо. Offload WED не эмулируется.
- Прерывания PHY не генерируются (изменения линка после загрузки видят
  только драйверы, работающие опросом).
- Нет packet engine EIP-97 и PCIe-устройств; PWM/I2C — заглушки.
- Скорость: примерно в 2 раза медленнее настоящего SoC 1,3 ГГц на обычном ПК (TCG).
