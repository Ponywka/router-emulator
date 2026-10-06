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

        // -M options: everything but the launcher's own keys
        public string MachineOptions()
        {
            var sb = new StringBuilder();
            foreach (var kv in Values) {
                if (kv.Key == "name" || kv.Key == "description" || kv.Key == "ram" || kv.Key == "nand-dir"
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
        CheckBox autoDesc;

        static readonly string[] PortIds = { "wan", "lan1", "lan2", "lan3", "lan4", "-" };
        // keys written by the editor (dropped when not applicable)
        static readonly List<string> Known = new List<string> {
            "name", "description", "gmac0", "ports", "gmac0-port", "gmac0-reset-gpio", "gmac1",
            "gmac1-port", "gmac1-reset-gpio", "nand", "ddr", "ram", "usb-port", "reset-gpio",
            "wps-gpio", "nand-dir" };

        public PresetForm(string presetDir, string root, Preset p)
        {
            this.presetDir = presetDir;
            this.root = root;
            preset = p;
            Text = p == null ? "New board preset" : "Board preset: " + p.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 610);
            Font = new Font("Segoe UI", 9f);

            int y = 12;
            name = new TextBox { Left = 150, Top = y, Width = 470 };
            Row("Name:", name, ref y);
            desc = new TextBox { Left = 150, Top = y, Width = 470 };
            Row("Description:", desc, ref y, 22);
            autoDesc = new CheckBox { Left = 150, Top = y, Width = 470, Text = "Generate the description from the hardware below" };
            Controls.Add(autoDesc);
            y += 30;

            var eth = new GroupBox { Left = 10, Top = y, Width = 620, Height = 238, Text = "Ethernet" };
            Controls.Add(eth);
            int gy = 22;
            gmac0 = Combo(eth, "GMAC0 (mac@0):", ref gy,
                new Choice("mt7531", "MT7531 switch (5 x 1G ports)"),
                new Choice("rtl8221b", "RTL8221B 2.5G PHY (Realtek)"),
                new Choice("yt8821", "YT8821 2.5G PHY (Motorcomm)"),
                new Choice("none", "Not connected"));
            eth.Controls.Add(new Label { Left = 10, Top = gy + 3, Width = 130, Text = "Switch ports 0..4:" });
            for (int i = 0; i < 5; i++) {
                swPort[i] = new ComboBox { Left = 140 + i * 94, Top = gy, Width = 88, DropDownStyle = ComboBoxStyle.DropDown };
                swPort[i].Items.AddRange(PortIds);
                eth.Controls.Add(swPort[i]);
            }
            gy += 30;
            gmac0Port = PortCombo(eth, "GMAC0 PHY port:", ref gy, out gmac0Rst);
            gy += 6;
            gmac1 = Combo(eth, "GMAC1 (mac@1):", ref gy,
                new Choice("rtl8221b", "RTL8221B 2.5G PHY (Realtek)"),
                new Choice("yt8821", "YT8821 2.5G PHY (Motorcomm)"),
                new Choice("gphy", "MT7981 built-in 1G PHY"),
                new Choice("none", "Not connected"));
            gmac1Port = PortCombo(eth, "GMAC1 PHY port:", ref gy, out gmac1Rst);
            eth.Controls.Add(new Label { Left = 140, Top = gy, Width = 470, Height = 34, ForeColor = Color.DimGray,
                Text = "Port names must match the firmware (device tree labels). The launcher connects "
                     + "its WAN choice to \"wan\" and its LAN choice to \"lan1\"." });
            y += eth.Height + 8;

            var mem = new GroupBox { Left = 10, Top = y, Width = 620, Height = 150, Text = "Memory, flash, USB" };
            Controls.Add(mem);
            gy = 22;
            ddr = Combo(mem, "RAM type:", ref gy,
                new Choice("ddr4", "DDR4"), new Choice("ddr3", "DDR3"));
            ram = Combo(mem, "RAM size:", ref gy,
                new Choice("256", "256 MB"), new Choice("512", "512 MB"), new Choice("1024", "1 GB"));
            nandSize = Combo(mem, "SPI-NAND:", ref gy,
                new Choice("128", "128 MB (Winbond W25N01GV)"), new Choice("256", "256 MB (Winbond W25N02KV)"));
            usbPort = Combo(mem, "USB port:", ref gy,
                new Choice("2", "USB 2.0"), new Choice("3", "USB 3.0"), new Choice("none", "None"));
            y += mem.Height + 8;

            var adv = new GroupBox { Left = 10, Top = y, Width = 620, Height = 56, Text = "Buttons (GPIO numbers)" };
            Controls.Add(adv);
            adv.Controls.Add(new Label { Left = 10, Top = 25, Width = 80, Text = "Reset:" });
            resetGpio = new NumericUpDown { Left = 90, Top = 22, Width = 60, Minimum = 0, Maximum = 100 };
            adv.Controls.Add(resetGpio);
            adv.Controls.Add(new Label { Left = 170, Top = 25, Width = 60, Text = "WPS:" });
            wpsGpio = new NumericUpDown { Left = 230, Top = 22, Width = 60, Minimum = 0, Maximum = 100 };
            adv.Controls.Add(wpsGpio);
            y += adv.Height + 8;

            nandDir = new TextBox { Left = 150, Top = y, Width = 380 };
            Row("NAND folder:", nandDir, ref y, 0);
            var browse = new Button { Left = 536, Top = y - 1, Width = 84, Height = 25, Text = "Browse..." };
            browse.Click += delegate {
                using (var d = new FolderBrowserDialog { SelectedPath = FullDir(nandDir.Text) }) {
                    if (d.ShowDialog(this) == DialogResult.OK) nandDir.Text = RelDir(d.SelectedPath);
                }
            };
            Controls.Add(browse);
            y += 40;

            var save = new Button { Left = 150, Top = y, Width = 110, Height = 30, Text = "Save" };
            var saveAs = new Button { Left = 266, Top = y, Width = 110, Height = 30, Text = "Save as new..." };
            var del = new Button { Left = 382, Top = y, Width = 110, Height = 30, Text = "Delete", Enabled = p != null };
            var cancel = new Button { Left = 510, Top = y, Width = 110, Height = 30, Text = "Cancel", DialogResult = DialogResult.Cancel };
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
            parent.Controls.Add(new Label { Left = 240, Top = y + 3, Width = 230, Text = "PHY reset GPIO (-1 = not wired):" });
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
            nandDir.Text = p.Get("nand-dir", "nand");
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
            parts.Add(ddr.Text + " " + ram.Text);
            parts.Add("NAND " + Val(nandSize) + " MB");
            parts.Add(Val(usbPort) == "none" ? "no USB" : usbPort.Text);
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
                if (seen.Contains(n)) return "Port name \"" + n + "\" is used twice.";
                seen.Add(n);
            }
            return null;
        }

        void DoSave(bool asNew)
        {
            var p = Build();
            if (p.Name.Length == 0) { MessageBox.Show(this, "Enter a name.", Text); return; }
            string err = CheckPorts(p);
            if (err != null) { MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (asNew) {
                p.FilePath = Path.Combine(presetDir, Preset.FileNameFor(p.Name));
                if (File.Exists(p.FilePath) &&
                    MessageBox.Show(this, "A preset file " + Path.GetFileName(p.FilePath) + " already exists. Replace it?",
                                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            } else {
                p.FilePath = preset.FilePath;
            }
            try {
                Directory.CreateDirectory(presetDir);
                p.Save();
            } catch (Exception e) {
                MessageBox.Show(this, "Cannot save the preset:\n" + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Result = p;
            DialogResult = DialogResult.OK;
        }

        void DoDelete()
        {
            if (MessageBox.Show(this, "Delete the preset \"" + preset.Name + "\" (" + Path.GetFileName(preset.FilePath)
                                + ")? The NAND folder is not touched.", Text,
                                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try {
                File.Delete(preset.FilePath);
            } catch (Exception e) {
                MessageBox.Show(this, "Cannot delete:\n" + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Deleted = true;
            DialogResult = DialogResult.OK;
        }
    }
}
