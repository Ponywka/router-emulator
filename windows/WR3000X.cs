// WR3000X emulator launcher for Windows.
//
// Starts qemu\qemu-system-aarch64.exe with the selected Cudy board, NAND
// folder, network attachments (Npcap adapter / NAT / host access) and USB
// folder; the router's serial console opens in its own window.  Board
// buttons (reset, WPS) are sent through QMP.
//
// Build (Mono or .NET Framework csc):
//   mcs -target:winexe -out:WR3000X.exe -r:System.Windows.Forms.dll
//       -r:System.Drawing.dll WR3000X.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WR3000X
{
    class Board
    {
        public string Machine, Name, NandDir;
        public bool WanOnPhy;   // WAN through RTL8221B on GMAC2
        public override string ToString() { return Name; }
    }

    class NetChoice
    {
        public string Kind;     // "nat", "host", "none", "pcap"
        public string Device;   // pcap device name
        public string Label;
        public override string ToString() { return Label; }
    }

    class MainForm : Form
    {
        static readonly Board[] Boards = {
            new Board { Machine = "cudy-wr3000p", Name = "Cudy WR3000P v1 (DDR4, 2.5G WAN)", NandDir = "nand", WanOnPhy = true },
            new Board { Machine = "cudy-wr3000s", Name = "Cudy WR3000S v1 (DDR3, 1G WAN)", NandDir = "nand-wr3000s" },
            new Board { Machine = "cudy-wr3000u", Name = "Cudy WR3000U v1 (DDR3, 256MB NAND)", NandDir = "nand-wr3000u" },
            new Board { Machine = "cudy-wbr3000uax", Name = "Cudy WBR3000UAX v1 (DDR3, 1G WAN)", NandDir = "nand-wbr3000uax" },
        };

        readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        ComboBox board, wan, lan;
        TextBox nand, usb;
        CheckBox useUsb, gpioLog;
        Button start, btnReset, btnFactory, btnWps, btnPower;
        Label status;
        Process qemu;
        int qmpPort;
        Dictionary<string, string> cfg = new Dictionary<string, string>();

        string CfgPath { get { return Path.Combine(root, "WR3000X.ini"); } }

        MainForm()
        {
            Text = "WR3000X router emulator (MediaTek MT7981)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            ClientSize = new Size(620, 400);
            Font = new Font("Segoe UI", 9f);
            LoadCfg();

            int y = 14;
            AddLabel("Model:", y);
            board = new ComboBox { Left = 130, Top = y, Width = 470, DropDownStyle = ComboBoxStyle.DropDownList };
            board.Items.AddRange(Boards);
            Controls.Add(board);
            y += 34;

            AddLabel("NAND folder:", y);
            nand = new TextBox { Left = 130, Top = y, Width = 380 };
            Controls.Add(nand);
            AddBrowse(nand, y);
            y += 22;
            Controls.Add(new Label {
                Left = 130, Top = y, Width = 470, Height = 18, ForeColor = Color.DimGray,
                Text = "Files *.mtd0.BL2.bin, *.mtd1.*.bin ... are joined in mtd order into the flash."
            });
            y += 28;

            AddLabel("WAN port:", y);
            wan = new ComboBox { Left = 130, Top = y, Width = 470, DropDownStyle = ComboBoxStyle.DropDownList };
            Controls.Add(wan);
            y += 34;

            AddLabel("LAN1 port:", y);
            lan = new ComboBox { Left = 130, Top = y, Width = 470, DropDownStyle = ComboBoxStyle.DropDownList };
            Controls.Add(lan);
            y += 34;

            useUsb = new CheckBox { Left = 14, Top = y, Width = 115, Text = "USB folder:" };
            Controls.Add(useUsb);
            usb = new TextBox { Left = 130, Top = y, Width = 380 };
            Controls.Add(usb);
            AddBrowse(usb, y);
            y += 34;

            gpioLog = new CheckBox { Left = 130, Top = y, Width = 400, Text = "Show LED / GPIO changes in the console" };
            Controls.Add(gpioLog);
            y += 36;

            start = new Button { Left = 130, Top = y, Width = 150, Height = 30, Text = "Power on" };
            start.Click += delegate { if (qemu == null || qemu.HasExited) Start(); else Stop(); };
            Controls.Add(start);
            btnPower = new Button { Left = 290, Top = y, Width = 150, Height = 30, Text = "Power cycle", Enabled = false };
            btnPower.Click += delegate { Qmp("{\"execute\":\"system_reset\"}"); };
            Controls.Add(btnPower);
            y += 40;

            btnReset = new Button { Left = 130, Top = y, Width = 150, Height = 28, Text = "Reset: short (reboot)", Enabled = false };
            btnReset.Click += delegate { PressButton("reset-button", 500); };
            Controls.Add(btnReset);
            btnFactory = new Button { Left = 290, Top = y, Width = 150, Height = 28, Text = "Reset: 10 s (factory)", Enabled = false };
            btnFactory.Click += delegate {
                if (MessageBox.Show(this, "Holding reset for 10 s makes OpenWrt erase all settings "
                        + "(factory reset). Continue?", Text, MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) == DialogResult.Yes)
                    PressButton("reset-button", 10000);
            };
            Controls.Add(btnFactory);
            btnWps = new Button { Left = 450, Top = y, Width = 150, Height = 28, Text = "WPS button", Enabled = false };
            btnWps.Click += delegate { PressButton("wps-button", 1000); };
            Controls.Add(btnWps);
            y += 38;

            status = new Label { Left = 14, Top = y, Width = 590, Height = 40, ForeColor = Color.DarkBlue };
            Controls.Add(status);

            FillNetworks();
            board.SelectedIndexChanged += delegate { OnBoardChanged(); };
            int bi = 0;
            for (int i = 0; i < Boards.Length; i++)
                if (Get("board") == Boards[i].Machine) bi = i;
            board.SelectedIndex = bi;
            nand.Text = Get("nand", Path.Combine(root, Boards[bi].NandDir));
            usb.Text = Get("usb", Path.Combine(root, "usb"));
            useUsb.Checked = Get("useusb", "1") == "1";
            gpioLog.Checked = Get("gpiolog", "0") == "1";
            Select(wan, Get("wan", "nat"));
            Select(lan, Get("lan", "host"));
            FormClosing += delegate { SaveCfg(); };
            status.Text = NpcapInstalled()
                ? "Ready."
                : "Npcap is not installed: only NAT / host access are available. "
                  + "Install it from https://npcap.com to attach router ports to a network adapter.";
        }

        void AddLabel(string text, int y)
        {
            Controls.Add(new Label { Left = 14, Top = y + 3, Width = 115, Text = text });
        }

        void AddBrowse(TextBox box, int y)
        {
            var b = new Button { Left = 516, Top = y - 1, Width = 84, Height = 25, Text = "Browse..." };
            b.Click += delegate {
                using (var d = new FolderBrowserDialog { SelectedPath = box.Text }) {
                    if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath;
                }
            };
            Controls.Add(b);
        }

        void OnBoardChanged()
        {
            var b = (Board)board.SelectedItem;
            string cur = nand.Text;
            foreach (var o in Boards) {
                if (cur == Path.Combine(root, o.NandDir) || cur == "") {
                    nand.Text = Path.Combine(root, b.NandDir);
                    break;
                }
            }
        }

        static bool NpcapInstalled()
        {
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return File.Exists(Path.Combine(sys, "Npcap", "wpcap.dll"))
                || File.Exists(Path.Combine(sys, "wpcap.dll"));
        }

        void FillNetworks()
        {
            var adapters = new List<NetChoice>();
            if (NpcapInstalled()) {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;
                    string wifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                        ? " - Wi-Fi: usually cannot bridge" : "";
                    adapters.Add(new NetChoice {
                        Kind = "pcap", Device = "\\Device\\NPF_" + ni.Id,
                        Label = "Bridge to: " + ni.Name + " (" + ni.Description + ")" + wifi
                    });
                }
            }
            wan.Items.Add(new NetChoice { Kind = "nat", Label = "NAT through this PC (router WAN gets 10.0.2.15)" });
            foreach (var a in adapters) wan.Items.Add(a);
            wan.Items.Add(new NetChoice { Kind = "none", Label = "Not connected" });

            lan.Items.Add(new NetChoice { Kind = "host", Label = "This PC only: http://127.0.0.1:8080, ssh 127.0.0.1:8022" });
            foreach (var a in adapters)
                lan.Items.Add(new NetChoice { Kind = a.Kind, Device = a.Device,
                    Label = a.Label + "  (router DHCP server becomes visible there!)" });
            lan.Items.Add(new NetChoice { Kind = "none", Label = "Not connected" });
        }

        static void Select(ComboBox box, string key)
        {
            for (int i = 0; i < box.Items.Count; i++) {
                var c = (NetChoice)box.Items[i];
                if (c.Kind == key || c.Device == key) { box.SelectedIndex = i; return; }
            }
            box.SelectedIndex = 0;
        }

        static string Key(NetChoice c) { return c.Kind == "pcap" ? c.Device : c.Kind; }

        static string Esc(string s) { return s.Replace(",", ",,"); }

        static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char ch in a) {
                if (ch == '\\') { bs++; continue; }
                if (ch == '"') { sb.Append('\\', bs * 2 + 1); bs = 0; sb.Append('"'); continue; }
                sb.Append('\\', bs); bs = 0; sb.Append(ch);
            }
            sb.Append('\\', bs * 2).Append('"');
            return sb.ToString();
        }

        string NetArgs(string id, NetChoice c)
        {
            switch (c.Kind) {
            case "nat":
                return "-netdev user,id=" + id;
            case "host":
                return "-netdev user,id=" + id + ",net=192.168.1.0/24,host=192.168.1.250,"
                    + "dhcpstart=192.168.1.251,restrict=on,"
                    + "hostfwd=tcp:127.0.0.1:8080-192.168.1.1:80,"
                    + "hostfwd=tcp:127.0.0.1:8443-192.168.1.1:443,"
                    + "hostfwd=tcp:127.0.0.1:8022-192.168.1.1:22";
            case "pcap":
                return "-netdev pcap,id=" + id + ",ifname=" + Esc(c.Device);
            }
            return null;
        }

        void Start()
        {
            string exe = Path.Combine(root, "qemu", "qemu-system-aarch64.exe");
            if (!File.Exists(exe)) { Error("Not found: " + exe); return; }
            if (!Directory.Exists(nand.Text)) { Error("NAND folder does not exist:\n" + nand.Text); return; }
            var b = (Board)board.SelectedItem;
            var w = (NetChoice)wan.SelectedItem;
            var l = (NetChoice)lan.SelectedItem;
            if (w.Kind == "pcap" && l.Kind == "pcap" && w.Device == l.Device &&
                MessageBox.Show(this, "WAN and LAN are bridged to the same adapter. The router's DHCP "
                    + "server will answer clients of that network. Continue?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            qmpPort = 44000 + new Random().Next(1000);
            var args = new List<string> {
                "-M", b.Machine + ",nand-dir=" + Esc(nand.Text) + (gpioLog.Checked ? ",gpio-log=on" : ""),
                "-nographic",
                "-qmp", "tcp:127.0.0.1:" + qmpPort + ",server=on,wait=off",
            };
            string a;
            if ((a = NetArgs("wan", w)) != null) args.AddRange(SplitArg(a));
            if ((a = NetArgs("lan1", l)) != null) args.AddRange(SplitArg(a));
            if (useUsb.Checked) {
                if (!Directory.Exists(usb.Text)) Directory.CreateDirectory(usb.Text);
                args.Add("-blockdev");
                args.Add("driver=vvfat,node-name=usbstick,dir=" + Esc(usb.Text) + ",rw=on,fat-type=16");
                args.Add("-device");
                args.Add("usb-storage,drive=usbstick,removable=on");
            }
            var sb = new StringBuilder();
            foreach (var s in args) { if (sb.Length > 0) sb.Append(' '); sb.Append(Quote(s)); }

            try {
                File.WriteAllText(Path.Combine(root, "last-command.txt"),
                                  Quote(exe) + " " + sb + Environment.NewLine);
            } catch (Exception) { }

            // Keep the console open after QEMU exits so errors stay readable.
            var psi = new ProcessStartInfo {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = "/c \"title " + b.Name + " - serial console (Ctrl-A X to quit) & "
                    + Quote(exe) + " " + sb + " & echo. & pause\"",
                UseShellExecute = false,
                WorkingDirectory = Path.Combine(root, "qemu"),
            };
            try {
                qemu = Process.Start(psi);
            } catch (Exception e) {
                Error(e.Message);
                return;
            }
            SaveCfg();
            SetRunning(true);
            status.Text = "Running " + b.Machine + ". Console window: router serial port 115200 8N1.";
            var started = DateTime.Now;
            int misses = 0;
            var t = new System.Windows.Forms.Timer { Interval = 2000 };
            t.Tick += delegate {
                // the console wrapper waits for a key after QEMU exits, so
                // also watch QEMU itself through its QMP port
                bool alive = qemu != null && !qemu.HasExited;
                if (alive && (DateTime.Now - started).TotalSeconds > 10)
                    misses = QmpAlive() ? 0 : misses + 1;
                if (!alive || misses >= 2) {
                    t.Stop();
                    SetRunning(false);
                    status.Text = "Stopped.";
                }
            };
            t.Start();
        }

        bool QmpAlive()
        {
            try {
                using (var c = new TcpClient()) {
                    var ar = c.BeginConnect("127.0.0.1", qmpPort, null, null);
                    return ar.AsyncWaitHandle.WaitOne(500) && c.Connected;
                }
            } catch (Exception) {
                return false;
            }
        }

        static IEnumerable<string> SplitArg(string a)
        {
            int sp = a.IndexOf(' ');
            return new[] { a.Substring(0, sp), a.Substring(sp + 1) };
        }

        void Stop()
        {
            Qmp("{\"execute\":\"quit\"}");
        }

        void SetRunning(bool on)
        {
            start.Text = on ? "Power off" : "Power on";
            btnReset.Enabled = btnFactory.Enabled = btnWps.Enabled = btnPower.Enabled = on;
            board.Enabled = nand.Enabled = wan.Enabled = lan.Enabled = usb.Enabled = useUsb.Enabled = gpioLog.Enabled = !on;
        }

        void PressButton(string prop, int ms)
        {
            new Thread(() => {
                Qmp("{\"execute\":\"qom-set\",\"arguments\":{\"path\":\"/machine/pinctrl\",\"property\":\"" + prop + "\",\"value\":true}}");
                Thread.Sleep(ms);
                Qmp("{\"execute\":\"qom-set\",\"arguments\":{\"path\":\"/machine/pinctrl\",\"property\":\"" + prop + "\",\"value\":false}}");
            }) { IsBackground = true }.Start();
        }

        void Qmp(string cmd)
        {
            try {
                using (var c = new TcpClient("127.0.0.1", qmpPort))
                using (var s = c.GetStream()) {
                    var r = new StreamReader(s);
                    var wr = new StreamWriter(s) { AutoFlush = true, NewLine = "\r\n" };
                    r.ReadLine();                                   // greeting
                    wr.WriteLine("{\"execute\":\"qmp_capabilities\"}");
                    r.ReadLine();
                    wr.WriteLine(cmd);
                    if (!cmd.Contains("\"quit\"")) r.ReadLine();
                }
            } catch (Exception) {
                // QEMU gone or still starting
            }
        }

        void Error(string msg)
        {
            MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        string Get(string k, string def = "")
        {
            string v;
            return cfg.TryGetValue(k, out v) ? v : def;
        }

        void LoadCfg()
        {
            try {
                foreach (var line in File.ReadAllLines(CfgPath)) {
                    int eq = line.IndexOf('=');
                    if (eq > 0) cfg[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            } catch (Exception) { }
        }

        void SaveCfg()
        {
            try {
                File.WriteAllLines(CfgPath, new[] {
                    "board=" + ((Board)board.SelectedItem).Machine,
                    "nand=" + nand.Text,
                    "usb=" + usb.Text,
                    "useusb=" + (useUsb.Checked ? "1" : "0"),
                    "gpiolog=" + (gpioLog.Checked ? "1" : "0"),
                    "wan=" + Key((NetChoice)wan.SelectedItem),
                    "lan=" + Key((NetChoice)lan.SelectedItem),
                });
            } catch (Exception) { }
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new MainForm());
        }
    }
}
