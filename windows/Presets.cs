// Board presets (presets\*.ini) and the preset editor ("constructor").
//
// A preset file:
//   [preset]
//   name=...            shown in the model list
//   description=...     hardware summary
//   ram=512             MB (-m)
//   nand-dir=nand-xxx   default NAND folder (relative to the program folder)
//   openwrt=...         OpenWrt device profile the package's NAND folder is
//                       built from (build-windows.sh; ignored here)
//   key=value           every other key is a mt7981-router machine option
//                       (gmac0, gmac1, ports, nand, ddr, usb-port, ...)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MT7981
{
    class Preset
    {
        public string FilePath;
        // ordered key/value pairs as in the file
        public List<KeyValuePair<string, string>> Values = new List<KeyValuePair<string, string>>();

        public string Get(string k, string def = "")
        {
            foreach (var kv in Values) if (kv.Key == k) return kv.Value;
            return def;
        }

        public void Set(string k, string v)
        {
            for (int i = 0; i < Values.Count; i++) {
                if (Values[i].Key == k) { Values[i] = new KeyValuePair<string, string>(k, v); return; }
            }
            Values.Add(new KeyValuePair<string, string>(k, v));
        }

        public string Name { get { return Get("name", Path.GetFileNameWithoutExtension(FilePath)); } }
        public string Description { get { return Get("description"); } }
        public int RamMB { get { int r; return int.TryParse(Get("ram", "512"), out r) ? r : 512; } }
        public bool HasUsb { get { return Get("usb-port", "2") != "none"; } }

        // LAN1 "This PC only": router LAN address and port forwards
        // "pcport:routerport,..." from 127.0.0.1 to the router
        public const string DefaultLanIp = "192.168.1.1", DefaultForwards = "8080:80,8443:443,8022:22";
        public string LanIp { get { return Get("lan-ip", DefaultLanIp); } }
        public string LanForwards { get { return Get("lan-forwards", DefaultForwards); } }

        public static bool ValidIp(string s)
        {
            var p = s.Trim().Split('.');
            if (p.Length != 4) return false;
            foreach (var x in p) { int v; if (!int.TryParse(x, out v) || v < 0 || v > 255 || x.Length == 0) return false; }
            int last = int.Parse(p[3]);
            return last > 0 && last < 255;
        }

        // null on a syntax error
        public static List<int[]> ParseForwards(string s)
        {
            var list = new List<int[]>();
            foreach (var item in s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
                var pr = item.Split(':');
                int a, b;
                if (pr.Length != 2 || !int.TryParse(pr[0], out a) || !int.TryParse(pr[1], out b) ||
                    a < 1 || a > 65535 || b < 1 || b > 65535)
                    return null;
                list.Add(new[] { a, b });
            }
            return list;
        }

        // "127.0.0.1:8080/8443/8022 -> 192.168.1.1:80/443/22"
        public string ForwardSummary()
        {
            var pc = new List<string>();
            var rt = new List<string>();
            foreach (var f in ParseForwards(LanForwards) ?? new List<int[]>()) {
                pc.Add(f[0].ToString());
                rt.Add(f[1].ToString());
            }
            if (pc.Count == 0) return "-";
            return "127.0.0.1:" + string.Join("/", pc.ToArray()) + " -> " + LanIp + ":" + string.Join("/", rt.ToArray());
        }

        // QEMU user-mode network on LAN1: a virtual PC in the router's /24
        // (its own addresses chosen not to clash with the router), no
        // outgoing connections, only the forwards from this PC's loopback
        public string HostOnlyNetdev(string id)
        {
            string ip = ValidIp(LanIp) ? LanIp.Trim() : DefaultLanIp;
            string net = ip.Substring(0, ip.LastIndexOf('.') + 1);
            int router = int.Parse(ip.Substring(ip.LastIndexOf('.') + 1));
            var free = new List<int>();
            for (int i = 250; i > 0 && free.Count < 3; i--) if (i != router) free.Add(i);
            var sb = new StringBuilder("-netdev user,id=" + id + ",net=" + net + "0/24,host=" + net + free[0]
                + ",dns=" + net + free[1] + ",dhcpstart=" + net + free[2] + ",restrict=on");
            foreach (var f in ParseForwards(LanForwards) ?? new List<int[]>())
                sb.Append(",hostfwd=tcp:127.0.0.1:" + f[0] + "-" + ip + ":" + f[1]);
            return sb.ToString();
        }

        // -M options: everything but the launcher's own keys
        public string MachineOptions()
        {
            var sb = new StringBuilder();
            foreach (var kv in Values) {
                if (kv.Key == "name" || kv.Key == "description" || kv.Key == "ram" || kv.Key == "nand-dir"
                    || kv.Key.StartsWith("lan-")       // LAN1 "This PC only" network
                    || kv.Key.StartsWith("openwrt"))   // used by the package build only
                    continue;
                sb.Append(',').Append(kv.Key).Append('=').Append(kv.Value.Replace(",", ",,"));
            }
            return sb.ToString();
        }

        public override string ToString() { return Name; }

        public static Preset Load(string path)
        {
            var p = new Preset { FilePath = path };
            foreach (var raw in File.ReadAllLines(path)) {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq > 0) p.Set(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
            }
            return p;
        }

        public void Save()
        {
            var lines = new List<string> {
                "; Board preset for the MT7981 Router Emulator.",
                "; Keys other than name/description/ram/nand-dir are -M machine options.",
                "[preset]",
            };
            foreach (var kv in Values) lines.Add(kv.Key + "=" + kv.Value);
            File.WriteAllLines(FilePath, lines.ToArray());
        }

        public static List<Preset> LoadAll(string dir)
        {
            var list = new List<Preset>();
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*.ini")) {
                try { list.Add(Load(f)); } catch (Exception) { }
            }
            list.Sort(delegate (Preset a, Preset b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        public static string FileNameFor(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name.ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '-');
            string s = sb.ToString().Trim('-');
            while (s.Contains("--")) s = s.Replace("--", "-");
            return (s.Length > 0 ? s : "preset") + ".ini";
        }
    }

    class Choice
    {
        public string Value, Label;
        public Choice(string v, string l) { Value = v; Label = l; }
        public override string ToString() { return Label; }
    }

    // The preset editor
    class PresetForm : Form
    {
        readonly string presetDir, root;
        Preset preset;          // null: new preset
        public Preset Result;   // saved preset (null if deleted / cancelled)
        public bool Deleted;

        TextBox name, desc, nandDir;
        ComboBox gmac0, gmac1, gmac0Port, gmac1Port, nandSize, ddr, ram, usbPort;
        ComboBox[] swPort = new ComboBox[5];
        NumericUpDown gmac0Rst, gmac1Rst, resetGpio, wpsGpio;
        CheckBox resetHigh, wpsHigh;
        TextBox lanIp, lanFwd;
        CheckBox autoDesc;

        static readonly string[] PortIds = { "wan", "lan1", "lan2", "lan3", "lan4", "-" };
        // keys written by the editor (dropped when not applicable)
        static readonly List<string> Known = new List<string> {
            "name", "description", "gmac0", "ports", "gmac0-port", "gmac0-reset-gpio", "gmac1",
            "gmac1-port", "gmac1-reset-gpio", "nand", "ddr", "ram", "usb-port", "reset-gpio",
            "wps-gpio", "reset-active-high", "wps-active-high", "lan-ip", "lan-forwards", "nand-dir" };

        public PresetForm(string presetDir, string root, Preset p)
        {
            this.presetDir = presetDir;
            this.root = root;
            preset = p;
            Text = p == null ? L.T("ed.title_new", "New board preset") : L.F("ed.title", "Board preset: {0}", p.Name);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 610);
            Font = new Font("Segoe UI", 9f);

            int y = 12;
            name = new TextBox { Left = 150, Top = y, Width = 470 };
            Row(L.T("ed.name", "Name:"), name, ref y);
            desc = new TextBox { Left = 150, Top = y, Width = 470 };
            Row(L.T("ed.description", "Description:"), desc, ref y, 22);
            autoDesc = new CheckBox { Left = 150, Top = y, Width = 470, Text = L.T("ed.auto_desc", "Generate the description from the hardware below") };
            Controls.Add(autoDesc);
            y += 30;

            var eth = new GroupBox { Left = 10, Top = y, Width = 620, Height = 238, Text = "Ethernet" };
            Controls.Add(eth);
            int gy = 22;
            gmac0 = Combo(eth, "GMAC0 (mac@0):", ref gy,
                new Choice("mt7531", L.T("ed.mt7531", "MT7531 switch (5 x 1G ports)")),
                new Choice("rtl8221b", L.T("ed.rtl8221b", "RTL8221B 2.5G PHY (Realtek)")),
                new Choice("yt8821", L.T("ed.yt8821", "YT8821 2.5G PHY (Motorcomm)")),
                new Choice("none", L.T("ed.not_connected", "Not connected")));
            eth.Controls.Add(new Label { Left = 10, Top = gy + 3, Width = 130, Text = L.T("ed.switch_ports", "Switch ports 0..4:") });
            for (int i = 0; i < 5; i++) {
                swPort[i] = new ComboBox { Left = 140 + i * 94, Top = gy, Width = 88, DropDownStyle = ComboBoxStyle.DropDown };
                swPort[i].Items.AddRange(PortIds);
                eth.Controls.Add(swPort[i]);
            }
            gy += 30;
            gmac0Port = PortCombo(eth, L.F("ed.phy_port", "{0} PHY port:", "GMAC0"), ref gy, out gmac0Rst);
            gy += 6;
            gmac1 = Combo(eth, "GMAC1 (mac@1):", ref gy,
                new Choice("rtl8221b", L.T("ed.rtl8221b", "RTL8221B 2.5G PHY (Realtek)")),
                new Choice("yt8821", L.T("ed.yt8821", "YT8821 2.5G PHY (Motorcomm)")),
                new Choice("gphy", L.T("ed.gphy", "MT7981 built-in 1G PHY")),
                new Choice("none", L.T("ed.not_connected", "Not connected")));
            gmac1Port = PortCombo(eth, L.F("ed.phy_port", "{0} PHY port:", "GMAC1"), ref gy, out gmac1Rst);
            eth.Controls.Add(new Label { Left = 140, Top = gy, Width = 470, Height = 34, ForeColor = Color.DimGray,
                Text = L.T("ed.port_note", "Port names must match the firmware (device tree labels). The launcher connects "
                     + "its WAN choice to \"wan\" and its LAN choice to \"lan1\".") });
            y += eth.Height + 8;

            var mem = new GroupBox { Left = 10, Top = y, Width = 620, Height = 150, Text = L.T("ed.memory", "Memory, flash, USB") };
            Controls.Add(mem);
            gy = 22;
            ddr = Combo(mem, L.T("ed.ram_type", "RAM type:"), ref gy,
                new Choice("ddr4", "DDR4"), new Choice("ddr3", "DDR3"));
            string mb = L.T("ed.mb", "MB"), gb = L.T("ed.gb", "GB");
            ram = Combo(mem, L.T("ed.ram_size", "RAM size:"), ref gy,
                new Choice("256", "256 " + mb), new Choice("512", "512 " + mb), new Choice("1024", "1 " + gb));
            nandSize = Combo(mem, "SPI-NAND:", ref gy,
                new Choice("128", "128 " + mb + " (Winbond W25N01GV)"), new Choice("256", "256 " + mb + " (Winbond W25N02KV)"));
            usbPort = Combo(mem, L.T("ed.usb_port", "USB port:"), ref gy,
                new Choice("2", "USB 2.0"), new Choice("3", "USB 3.0"), new Choice("none", L.T("ed.none", "None")));
            y += mem.Height + 8;

            var adv = new GroupBox { Left = 10, Top = y, Width = 620, Height = 56, Text = L.T("ed.buttons", "Buttons (GPIO numbers)") };
            Controls.Add(adv);
            adv.Controls.Add(new Label { Left = 10, Top = 25, Width = 60, Text = L.T("ed.reset", "Reset:") });
            resetGpio = new NumericUpDown { Left = 70, Top = 22, Width = 55, Minimum = 0, Maximum = 100 };
            adv.Controls.Add(resetGpio);
            resetHigh = new CheckBox { Left = 135, Top = 23, Width = 160, Text = L.T("ed.active_high", "active high") };
            adv.Controls.Add(resetHigh);
            adv.Controls.Add(new Label { Left = 300, Top = 25, Width = 80, Text = L.T("ed.wps", "WPS / mesh:") });
            wpsGpio = new NumericUpDown { Left = 385, Top = 22, Width = 55, Minimum = 0, Maximum = 100 };
            adv.Controls.Add(wpsGpio);
            wpsHigh = new CheckBox { Left = 450, Top = 23, Width = 160, Text = L.T("ed.active_high", "active high") };
            adv.Controls.Add(wpsHigh);
            var tip = new ToolTip();
            string tipText = L.T("ed.active_high_tip", "Ticked: the GPIO reads 1 while the button is pressed.\n"
                + "Default (unticked): active low, the GPIO reads 0 while pressed.");
            tip.SetToolTip(resetHigh, tipText);
            tip.SetToolTip(wpsHigh, tipText);
            y += adv.Height + 8;

            var acc = new GroupBox { Left = 10, Top = y, Width = 620, Height = 90,
                Text = L.T("ed.pc_access", "Access from this PC (LAN1 \"This PC only\")") };
            Controls.Add(acc);
            acc.Controls.Add(new Label { Left = 10, Top = 25, Width = 130, Text = L.T("ed.lan_ip", "Router LAN IP:") });
            lanIp = new TextBox { Left = 140, Top = 22, Width = 120 };
            acc.Controls.Add(lanIp);
            acc.Controls.Add(new Label { Left = 280, Top = 25, Width = 110, Text = L.T("ed.forwards", "Port forwards:") });
            lanFwd = new TextBox { Left = 390, Top = 22, Width = 220 };
            acc.Controls.Add(lanFwd);
            acc.Controls.Add(new Label { Left = 140, Top = 50, Width = 470, Height = 34, ForeColor = Color.DimGray,
                Text = L.T("ed.forwards_hint", "PC port:router port, comma separated, e.g. 8080:80,8443:443,8022:22: "
                    + "http://127.0.0.1:8080 opens the router's port 80.") });
            y += acc.Height + 8;

            nandDir = new TextBox { Left = 150, Top = y, Width = 380 };
            Row(L.T("ed.nand_dir", "NAND folder:"), nandDir, ref y, 0);
            var browse = new Button { Left = 536, Top = y - 1, Width = 84, Height = 25, Text = L.T("main.browse", "Browse...") };
            browse.Click += delegate {
                using (var d = new FolderBrowserDialog { SelectedPath = FullDir(nandDir.Text) }) {
                    if (d.ShowDialog(this) == DialogResult.OK) nandDir.Text = RelDir(d.SelectedPath);
                }
            };
            Controls.Add(browse);
            y += 40;

            var save = new Button { Left = 150, Top = y, Width = 110, Height = 30, Text = L.T("ed.save", "Save") };
            var saveAs = new Button { Left = 266, Top = y, Width = 110, Height = 30, Text = L.T("ed.save_as", "Save as new...") };
            var del = new Button { Left = 382, Top = y, Width = 110, Height = 30, Text = L.T("ed.delete", "Delete"), Enabled = p != null };
            var cancel = new Button { Left = 510, Top = y, Width = 110, Height = 30, Text = L.T("ed.cancel", "Cancel"), DialogResult = DialogResult.Cancel };
            save.Click += delegate { DoSave(preset == null); };
            saveAs.Click += delegate { DoSave(true); };
            del.Click += delegate { DoDelete(); };
            Controls.Add(save); Controls.Add(saveAs); Controls.Add(del); Controls.Add(cancel);
            CancelButton = cancel;
            ClientSize = new Size(640, y + 44);

            gmac0.SelectedIndexChanged += delegate { UpdateEnabled(); };
            gmac1.SelectedIndexChanged += delegate { UpdateEnabled(); };
            autoDesc.CheckedChanged += delegate { desc.ReadOnly = autoDesc.Checked; UpdateDesc(); };
            foreach (Control c in new Control[] { gmac0, gmac1, gmac0Port, gmac1Port, ddr, ram, nandSize, usbPort })
                c.TextChanged += delegate { UpdateDesc(); };
            foreach (var c in swPort) c.TextChanged += delegate { UpdateDesc(); };

            Fill(p ?? Default());
            autoDesc.Checked = p == null;
            UpdateEnabled();
        }

        static Preset Default()
        {
            var p = new Preset();
            p.Set("name", "My MT7981 board");
            p.Set("gmac0", "mt7531");
            p.Set("ports", "lan1:lan2:lan3:lan4:-");
            p.Set("gmac1", "rtl8221b");
            p.Set("gmac1-port", "wan");
            p.Set("nand", "128");
            p.Set("ddr", "ddr4");
            p.Set("ram", "512");
            p.Set("usb-port", "2");
            return p;
        }

        void Row(string label, Control c, ref int y, int step = 30)
        {
            Controls.Add(new Label { Left = 14, Top = y + 3, Width = 135, Text = label });
            Controls.Add(c);
            y += step;
        }

        static ComboBox Combo(Control parent, string label, ref int y, params Choice[] items)
        {
            parent.Controls.Add(new Label { Left = 10, Top = y + 3, Width = 130, Text = label });
            var c = new ComboBox { Left = 140, Top = y, Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.AddRange(items);
            parent.Controls.Add(c);
            y += 30;
            return c;
        }

        static ComboBox PortCombo(Control parent, string label, ref int y, out NumericUpDown rst)
        {
            parent.Controls.Add(new Label { Left = 10, Top = y + 3, Width = 130, Text = label });
            var c = new ComboBox { Left = 140, Top = y, Width = 88, DropDownStyle = ComboBoxStyle.DropDown };
            c.Items.AddRange(PortIds);
            parent.Controls.Add(c);
            parent.Controls.Add(new Label { Left = 240, Top = y + 3, Width = 230, Text = L.T("ed.phy_reset", "PHY reset GPIO (-1 = not wired):") });
            rst = new NumericUpDown { Left = 470, Top = y, Width = 60, Minimum = -1, Maximum = 100, Value = -1 };
            parent.Controls.Add(rst);
            y += 30;
            return c;
        }

        static void SelectValue(ComboBox c, string v)
        {
            for (int i = 0; i < c.Items.Count; i++)
                if (((Choice)c.Items[i]).Value == v) { c.SelectedIndex = i; return; }
            c.SelectedIndex = 0;
        }

        // (null while Fill() is still selecting the other lists)
        static string Val(ComboBox c) { var ch = c.SelectedItem as Choice; return ch != null ? ch.Value : ""; }

        static int Int(string s, int def) { int v; return int.TryParse(s, out v) ? v : def; }

        // QEMU bool option values
        static bool IsOn(string v) { v = v.ToLowerInvariant(); return v == "on" || v == "true" || v == "yes" || v == "1"; }

        static decimal Clamp(NumericUpDown n, int v) { return Math.Max(n.Minimum, Math.Min(n.Maximum, v)); }

        void Fill(Preset p)
        {
            name.Text = p.Name;
            desc.Text = p.Description;
            SelectValue(gmac0, p.Get("gmac0", "mt7531"));
            SelectValue(gmac1, p.Get("gmac1", "rtl8221b"));
            string[] ports = p.Get("ports", "lan1:lan2:lan3:lan4").Split(':');
            for (int i = 0; i < 5; i++) swPort[i].Text = i < ports.Length ? ports[i] : "-";
            gmac0Port.Text = p.Get("gmac0-port", "lan1");
            gmac1Port.Text = p.Get("gmac1-port", "wan");
            gmac0Rst.Value = Clamp(gmac0Rst, Int(p.Get("gmac0-reset-gpio"), -1));
            gmac1Rst.Value = Clamp(gmac1Rst, Int(p.Get("gmac1-reset-gpio"), -1));
            SelectValue(ddr, p.Get("ddr", "ddr4"));
            SelectValue(ram, p.Get("ram", "512"));
            SelectValue(nandSize, p.Get("nand", "128"));
            SelectValue(usbPort, p.Get("usb-port", "2"));
            resetGpio.Value = Clamp(resetGpio, Int(p.Get("reset-gpio"), 1));
            wpsGpio.Value = Clamp(wpsGpio, Int(p.Get("wps-gpio"), 0));
            resetHigh.Checked = IsOn(p.Get("reset-active-high"));
            wpsHigh.Checked = IsOn(p.Get("wps-active-high"));
            nandDir.Text = p.Get("nand-dir", "nand");
            lanIp.Text = p.LanIp;
            lanFwd.Text = p.LanForwards;
        }

        void UpdateEnabled()
        {
            bool sw = Val(gmac0) == "mt7531";
            foreach (var c in swPort) c.Enabled = sw;
            gmac0Port.Enabled = ExtPhy(Val(gmac0));
            gmac0Rst.Enabled = ExtPhy(Val(gmac0));
            gmac1Port.Enabled = Val(gmac1) != "none";
            gmac1Rst.Enabled = ExtPhy(Val(gmac1));
            UpdateDesc();
        }

        // a 2.5G PHY chip with its own reset line
        static bool ExtPhy(string v) { return v == "rtl8221b" || v == "yt8821"; }

        static string PhyName(string v) { return v == "yt8821" ? "Motorcomm YT8821" : "RTL8221B"; }

        // e.g. "2.5G WAN RTL8221B, 4x1G LAN MT7531, DDR4 512 MB, NAND 128 MB, USB 2.0"
        string Summary()
        {
            var parts = new List<string>();
            int swn = 0; bool swWan = false;
            if (Val(gmac0) == "mt7531") {
                foreach (var c in swPort) {
                    string t = c.Text.Trim();
                    if (t == "" || t == "-") continue;
                    if (t == "wan") swWan = true; else swn++;
                }
            }
            if (ExtPhy(Val(gmac0))) parts.Add("2.5G " + gmac0Port.Text.Trim().ToUpperInvariant() + " " + PhyName(Val(gmac0)) + " on GMAC0");
            if (ExtPhy(Val(gmac1))) parts.Add("2.5G " + gmac1Port.Text.Trim().ToUpperInvariant() + " " + PhyName(Val(gmac1)));
            if (Val(gmac1) == "gphy") parts.Add("1G " + gmac1Port.Text.Trim().ToUpperInvariant() + " built-in PHY");
            if (Val(gmac0) == "mt7531") {
                if (swWan) parts.Add((swn + 1) + "x1G MT7531 (WAN = port " + WanPort() + ")");
                else parts.Add(swn + "x1G LAN MT7531");
            }
            // (English: the description is stored in the preset file)
            parts.Add(Val(ddr).ToUpperInvariant() + " " + (Val(ram) == "1024" ? "1 GB" : Val(ram) + " MB"));
            parts.Add("NAND " + Val(nandSize) + " MB");
            parts.Add(Val(usbPort) == "none" ? "no USB" : "USB " + Val(usbPort) + ".0");
            return string.Join(", ", parts.ToArray());
        }

        int WanPort()
        {
            for (int i = 0; i < 5; i++) if (swPort[i].Text.Trim() == "wan") return i;
            return -1;
        }

        void UpdateDesc()
        {
            if (autoDesc != null && autoDesc.Checked && gmac0.SelectedItem != null && gmac1.SelectedItem != null &&
                ddr.SelectedItem != null && ram.SelectedItem != null && nandSize.SelectedItem != null && usbPort.SelectedItem != null)
                desc.Text = Summary();
        }

        string FullDir(string d) { return Path.IsPathRooted(d) ? d : Path.Combine(root, d); }

        string RelDir(string d)
        {
            string r = root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return d.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? d.Substring(r.Length) : d;
        }

        Preset Build()
        {
            var p = new Preset();
            p.Set("name", name.Text.Trim());
            p.Set("description", desc.Text.Trim());
            p.Set("gmac0", Val(gmac0));
            if (Val(gmac0) == "mt7531") {
                var ports = new List<string>();
                foreach (var c in swPort) ports.Add(c.Text.Trim().Length > 0 ? c.Text.Trim() : "-");
                p.Set("ports", string.Join(":", ports.ToArray()));
            } else if (ExtPhy(Val(gmac0))) {
                p.Set("gmac0-port", gmac0Port.Text.Trim());
                if (gmac0Rst.Value >= 0) p.Set("gmac0-reset-gpio", gmac0Rst.Value.ToString());
            }
            p.Set("gmac1", Val(gmac1));
            if (Val(gmac1) != "none") {
                p.Set("gmac1-port", gmac1Port.Text.Trim());
                if (ExtPhy(Val(gmac1)) && gmac1Rst.Value >= 0)
                    p.Set("gmac1-reset-gpio", gmac1Rst.Value.ToString());
            }
            p.Set("nand", Val(nandSize));
            p.Set("ddr", Val(ddr));
            p.Set("ram", Val(ram));
            p.Set("usb-port", Val(usbPort));
            p.Set("reset-gpio", resetGpio.Value.ToString());
            p.Set("wps-gpio", wpsGpio.Value.ToString());
            if (resetHigh.Checked) p.Set("reset-active-high", "on");
            if (wpsHigh.Checked) p.Set("wps-active-high", "on");
            p.Set("lan-ip", lanIp.Text.Trim());
            p.Set("lan-forwards", lanFwd.Text.Trim().Replace(" ", ""));
            p.Set("nand-dir", nandDir.Text.Trim());
            // keep keys this editor does not know (openwrt=..., new options)
            if (preset != null) {
                foreach (var kv in preset.Values)
                    if (p.Get(kv.Key, null) == null && !Known.Contains(kv.Key)) p.Set(kv.Key, kv.Value);
            }
            return p;
        }

        // the same port name on two ports would leave one of them unconnected
        string CheckPorts(Preset p)
        {
            var seen = new List<string>();
            var names = new List<string>();
            if (p.Get("gmac0") == "mt7531") names.AddRange(p.Get("ports").Split(':'));
            if (ExtPhy(p.Get("gmac0"))) names.Add(p.Get("gmac0-port"));
            if (p.Get("gmac1") != "none") names.Add(p.Get("gmac1-port"));
            foreach (var n in names) {
                if (n == "-" || n == "") continue;
                if (seen.Contains(n)) return L.F("ed.port_twice", "Port name \"{0}\" is used twice.", n);
                seen.Add(n);
            }
            return null;
        }

        void DoSave(bool asNew)
        {
            var p = Build();
            if (p.Name.Length == 0) { MessageBox.Show(this, L.T("ed.enter_name", "Enter a name."), Text); return; }
            string err = CheckPorts(p);
            if (err == null && !Preset.ValidIp(p.LanIp))
                err = L.F("ed.bad_ip", "\"{0}\" is not an IPv4 address of a host.", p.LanIp);
            if (err == null && Preset.ParseForwards(p.LanForwards) == null)
                err = L.T("ed.bad_forwards", "Port forwards must look like 8080:80,8443:443,8022:22 (ports 1..65535).");
            if (err != null) { MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (asNew) {
                p.FilePath = Path.Combine(presetDir, Preset.FileNameFor(p.Name));
                if (File.Exists(p.FilePath) &&
                    MessageBox.Show(this, L.F("ed.replace", "A preset file {0} already exists. Replace it?", Path.GetFileName(p.FilePath)),
                                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            } else {
                p.FilePath = preset.FilePath;
            }
            try {
                Directory.CreateDirectory(presetDir);
                p.Save();
            } catch (Exception e) {
                MessageBox.Show(this, L.F("ed.cannot_save", "Cannot save the preset:\n{0}", e.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Result = p;
            DialogResult = DialogResult.OK;
        }

        void DoDelete()
        {
            if (MessageBox.Show(this, L.F("ed.ask_delete", "Delete the preset \"{0}\" ({1})? The NAND folder is not touched.",
                                preset.Name, Path.GetFileName(preset.FilePath)), Text,
                                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try {
                File.Delete(preset.FilePath);
            } catch (Exception e) {
                MessageBox.Show(this, L.F("ed.cannot_delete", "Cannot delete:\n{0}", e.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Deleted = true;
            DialogResult = DialogResult.OK;
        }
    }
}
