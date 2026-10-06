// MT7981 Router Emulator launcher for Windows.
//
// Starts qemu\qemu-system-aarch64.exe (machine mt7981-router) with the
// hardware of the selected board preset (presets\*.ini, editable in the
// preset editor), NAND folder, network attachments (Npcap adapter / NAT /
// host access) and USB folder; the router's serial console opens in its
// own window.  Board buttons (reset, WPS) are sent through QMP.
//
// Build: see build-windows.sh (mcs against the .NET Framework 4.8
// reference assemblies), or with csc.exe on Windows:
//   csc -target:winexe -out:MT7981.exe Launcher.cs Presets.cs Terminal.cs

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

namespace MT7981
{
    class NetChoice
    {
        public string Kind;     // "nat", "host", "none", "pcap"
        public string Device;   // pcap device name
        public string Label;
        public override string ToString() { return Label; }
    }

    class MainForm : Form
    {
        readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        string PresetDir { get { return Path.Combine(root, "presets"); } }
        ComboBox board, wan, lan;
        Label boardDesc;
        Button btnEdit;
        TextBox nand, usb, logs;
        CheckBox useUsb, gpioLog, useLogs, offOnPoweroff;
        Button start, btnReset, btnFactory, btnWps, btnPower, btnNew, btnTerm;
        Label status;
        Process qemu;
        TerminalForm term;
        int qmpPort;
        Dictionary<string, string> cfg = new Dictionary<string, string>();

        string CfgPath { get { return Path.Combine(root, "MT7981.ini"); } }

        MainForm()
        {
            Text = "MT7981 Router Emulator";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            ClientSize = new Size(620, 538);
            Font = new Font("Segoe UI", 9f);
            LoadCfg();

            int y = 14;
            AddLabel("Board preset:", y);
            board = new ComboBox { Left = 130, Top = y, Width = 290, DropDownStyle = ComboBoxStyle.DropDownList };
            Controls.Add(board);
            btnEdit = new Button { Left = 426, Top = y - 1, Width = 84, Height = 25, Text = "Edit..." };
            btnEdit.Click += delegate { EditPreset((Preset)board.SelectedItem); };
            Controls.Add(btnEdit);
            btnNew = new Button { Left = 516, Top = y - 1, Width = 84, Height = 25, Text = "New..." };
            btnNew.Click += delegate { EditPreset(null); };
            Controls.Add(btnNew);
            y += 28;
            boardDesc = new Label { Left = 130, Top = y, Width = 470, Height = 32, ForeColor = Color.DimGray };
            Controls.Add(boardDesc);
            y += 38;

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

            useLogs = new CheckBox { Left = 14, Top = y, Width = 115, Text = "Log folder:" };
            Controls.Add(useLogs);
            logs = new TextBox { Left = 130, Top = y, Width = 380 };
            Controls.Add(logs);
            AddBrowse(logs, y);
            y += 34;

            gpioLog = new CheckBox { Left = 130, Top = y, Width = 400, Text = "Show LED / GPIO changes in the console" };
            Controls.Add(gpioLog);
            y += 26;
            offOnPoweroff = new CheckBox { Left = 130, Top = y, Width = 470,
                Text = "Turn the emulator off on \"poweroff\" (real MT7981 reboots instead)" };
            Controls.Add(offOnPoweroff);
            y += 36;

            start = new Button { Left = 130, Top = y, Width = 150, Height = 30, Text = "Power on" };
            start.Click += delegate { if (qemu == null || qemu.HasExited) Start(0); else Stop(); };
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
            y += 34;

            // like the real board: hold reset, apply power, release after 10 s;
            // U-Boot then loads the recovery image via TFTP (OpenWrt U-Boot: server
            // 192.168.1.254; some vendor U-Boots: 192.168.1.88, file recovery.bin)
            var btnTftp = new Button { Left = 130, Top = y, Width = 310, Height = 28,
                                       Text = "Power + Reset: 10 s (TFTP recovery)" };
            btnTftp.Click += delegate {
                if (qemu == null || qemu.HasExited) {
                    Start(10000);
                } else {
                    Qmp("{\"execute\":\"qom-set\",\"arguments\":{\"path\":\"/machine/pinctrl\",\"property\":\"reset-hold-ms\",\"value\":10000}}");
                    Qmp("{\"execute\":\"system_reset\"}");
                }
            };
            Controls.Add(btnTftp);
            y += 38;

            btnTerm = new Button { Left = 450, Top = start.Top, Width = 150, Height = 30, Text = "Show console", Enabled = false };
            btnTerm.Click += delegate { if (term != null && !term.IsDisposed) { term.Show(); term.Activate(); } };
            Controls.Add(btnTerm);

            status = new Label { Left = 14, Top = y, Width = 590, Height = 32, ForeColor = Color.DarkBlue, AutoEllipsis = true };
            Controls.Add(status);
            ClientSize = new Size(620, status.Bottom + 6);   // fit the contents

            FillNetworks();
            board.SelectedIndexChanged += delegate { OnBoardChanged(); };
            FillPresets(Get("preset"));
            if (Get("nand") != "") nand.Text = Get("nand");
            usb.Text = Get("usb", Path.Combine(root, "usb"));
            useUsb.Checked = Get("useusb", "1") == "1";
            logs.Text = Get("logs", Path.Combine(root, "logs"));
            useLogs.Checked = Get("uselogs", "1") == "1";
            gpioLog.Checked = Get("gpiolog", "0") == "1";
            offOnPoweroff.Checked = Get("offonpoweroff", "1") == "1";
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

        string NandOf(Preset p)
        {
            string d = p.Get("nand-dir", "nand");
            return Path.IsPathRooted(d) ? d : Path.Combine(root, d);
        }

        // (re)load presets\*.ini and select the given file (name only)
        void FillPresets(string select)
        {
            board.Items.Clear();
            foreach (var p in Preset.LoadAll(PresetDir)) board.Items.Add(p);
            int idx = 0;
            for (int i = 0; i < board.Items.Count; i++)
                if (Path.GetFileName(((Preset)board.Items[i]).FilePath) == select) idx = i;
            if (board.Items.Count > 0) {
                board.SelectedIndex = idx;
            } else {
                boardDesc.Text = "No presets in " + PresetDir + ": create one with New...";
                OnBoardChanged();
            }
        }

        void OnBoardChanged()
        {
            var b = board.SelectedItem as Preset;
            btnEdit.Enabled = b != null;
            start.Enabled = b != null || (qemu != null && !qemu.HasExited);
            if (b == null) return;
            boardDesc.Text = b.Description;
            // follow the preset's NAND folder unless the user picked another one
            string cur = nand.Text;
            bool presetDir = cur == "";
            foreach (Preset o in board.Items)
                if (string.Equals(cur, NandOf(o), StringComparison.OrdinalIgnoreCase)) presetDir = true;
            if (presetDir) nand.Text = NandOf(b);
            useUsb.Enabled = b.HasUsb && (qemu == null || qemu.HasExited);
        }

        void EditPreset(Preset p)
        {
            using (var f = new PresetForm(PresetDir, root, p)) {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                string sel = f.Deleted ? "" : Path.GetFileName(f.Result.FilePath);
                if (f.Result != null && p != null && string.Equals(nand.Text, NandOf(p), StringComparison.OrdinalIgnoreCase))
                    nand.Text = NandOf(f.Result);
                FillPresets(sel);
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

        void Start(int resetHoldMs)
        {
            string exe = Path.Combine(root, "qemu", "qemu-system-aarch64.exe");
            if (!File.Exists(exe)) { Error("Not found: " + exe); return; }
            if (!Directory.Exists(nand.Text)) { Error("NAND folder does not exist:\n" + nand.Text); return; }
            var b = board.SelectedItem as Preset;
            if (b == null) { Error("Select or create a board preset first."); return; }
            var w = (NetChoice)wan.SelectedItem;
            var l = (NetChoice)lan.SelectedItem;
            if (w.Kind == "pcap" && l.Kind == "pcap" && w.Device == l.Device &&
                MessageBox.Show(this, "WAN and LAN are bridged to the same adapter. The router's DHCP "
                    + "server will answer clients of that network. Continue?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            qmpPort = FreePort();
            int conPort = FreePort();
            // serial console (+ QEMU monitor via Ctrl-A C) on a local socket,
            // shown in the built-in terminal; QEMU waits until it connects
            var args = new List<string> {
                "-M", "mt7981-router,nand-dir=" + Esc(nand.Text) + b.MachineOptions()
                      + (gpioLog.Checked ? ",gpio-log=on" : "")
                      + (resetHoldMs > 0 ? ",reset-hold=" + resetHoldMs : ""),
                "-m", b.RamMB + "M",
                "-display", "none",
                "-qmp", "tcp:127.0.0.1:" + qmpPort + ",server=on,wait=off",
                "-chardev", "socket,id=con,mux=on,host=127.0.0.1,port=" + conPort + ",server=on,wait=on",
                "-serial", "chardev:con",
                "-mon", "chardev=con",
            };
            ConsoleLog log = null;
            string logPath = null;
            if (useLogs.Checked) {
                try {
                    Directory.CreateDirectory(logs.Text);
                    logPath = Path.Combine(logs.Text, "console_"
                        + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".log");
                    log = new ConsoleLog(logPath);
                } catch (Exception e) {
                    Error("Cannot create the console log:\n" + e.Message);
                    return;
                }
            }
            string a;
            if ((a = NetArgs("wan", w)) != null) args.AddRange(SplitArg(a));
            if ((a = NetArgs("lan1", l)) != null) args.AddRange(SplitArg(a));
            if (useUsb.Checked && b.HasUsb) {
                if (!Directory.Exists(usb.Text)) Directory.CreateDirectory(usb.Text);
                args.Add("-blockdev");
                args.Add("driver=vvfat,node-name=usbstick,dir=" + Esc(usb.Text) + ",rw=on,fat-type=16");
                args.Add("-device");
                args.Add("usb-storage,drive=usbstick,removable=on,port=1");  // port=1: no auto-added full-speed hub
            }
            var sb = new StringBuilder();
            foreach (var s in args) { if (sb.Length > 0) sb.Append(' '); sb.Append(Quote(s)); }

            try {
                File.WriteAllText(Path.Combine(root, "last-command.txt"),
                                  Quote(exe) + " " + sb + Environment.NewLine);
            } catch (Exception) { }

            var psi = new ProcessStartInfo {
                FileName = exe,
                Arguments = sb.ToString(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.Combine(root, "qemu"),
            };
            var qemuErr = new StringBuilder();
            try {
                qemu = Process.Start(psi);
                qemu.ErrorDataReceived += (o, e) => { if (e.Data != null) lock (qemuErr) qemuErr.AppendLine(e.Data); };
                qemu.OutputDataReceived += (o, e) => { if (e.Data != null) lock (qemuErr) qemuErr.AppendLine(e.Data); };
                qemu.BeginErrorReadLine();
                qemu.BeginOutputReadLine();
            } catch (Exception e) {
                if (log != null) log.Dispose();
                Error(e.Message);
                return;
            }

            if (term != null && !term.IsDisposed) { term.AskClose = null; term.Close(); }
            term = new TerminalForm(b.Name);
            term.Log = log;
            term.AskClose = () => {
                if (qemu == null || qemu.HasExited) return true;
                var r = MessageBox.Show(term, "Power off the router?", Text,
                                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) Stop();
                return r == DialogResult.Yes;
            };
            term.PowerDown = () => { if (offOnPoweroff.Checked) Stop(); };
            term.Show();
            var proc = qemu;
            term.Connect(conPort, () => !proc.HasExited);

            SaveCfg();
            SetRunning(true);
            string shownLog = logPath;
            if (logPath != null && logPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                shownLog = logPath.Substring(root.Length).TrimStart('\\', '/');
            status.Text = "Running " + b.Name + "." + (logPath != null ? "\nLog: " + shownLog : "");
            var t = new System.Windows.Forms.Timer { Interval = 1000 };
            var myTerm = term;
            t.Tick += delegate {
                if (proc.HasExited) {
                    t.Stop();
                    SetRunning(false);
                    status.Text = "Stopped.";
                    string err;
                    lock (qemuErr) err = qemuErr.ToString().Trim();
                    if (!myTerm.IsDisposed)
                        myTerm.Message("\r\n\x1b[0m\x1b[33m[router powered off" +
                            (proc.ExitCode != 0 ? ", QEMU exit code " + proc.ExitCode : "") + "]\x1b[0m\r\n" +
                            // QEMU's stderr only matters when it failed
                            (proc.ExitCode != 0 && err.Length > 0
                                ? "\x1b[31m" + err.Replace("\n", "\r\n") + "\x1b[0m\r\n" : ""));
                }
            };
            t.Start();
        }

        static int FreePort()
        {
            var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            int p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
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
            btnReset.Enabled = btnFactory.Enabled = btnWps.Enabled = btnPower.Enabled = btnTerm.Enabled = on;
            board.Enabled = nand.Enabled = wan.Enabled = lan.Enabled = usb.Enabled = useUsb.Enabled = gpioLog.Enabled = !on;
            btnEdit.Enabled = btnNew.Enabled = !on;
            if (!on) OnBoardChanged();
            logs.Enabled = useLogs.Enabled = !on;
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
                    "preset=" + (board.SelectedItem != null ? Path.GetFileName(((Preset)board.SelectedItem).FilePath) : ""),
                    "nand=" + nand.Text,
                    "usb=" + usb.Text,
                    "useusb=" + (useUsb.Checked ? "1" : "0"),
                    "logs=" + logs.Text,
                    "uselogs=" + (useLogs.Checked ? "1" : "0"),
                    "gpiolog=" + (gpioLog.Checked ? "1" : "0"),
                    "offonpoweroff=" + (offOnPoweroff.Checked ? "1" : "0"),
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
