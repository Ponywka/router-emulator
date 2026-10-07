// User interface languages (languages\*.ini).
//
// A language file:
//   [language]
//   name=Русский            shown in the language list
//   [strings]
//   main.power_on=Включить  key=text; \n = new line, {0} = parameter
//
// Every text has an English default in the code; a missing key falls back
// to it, so a new language file can be partial.  en.ini lists all keys.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RouterEmulator
{
    class LangInfo
    {
        public string FilePath, Name;
        public override string ToString() { return Name; }
    }

    static class L
    {
        static Dictionary<string, string> strings = new Dictionary<string, string>();

        // text for key in the current language, else the English default
        public static string T(string key, string en)
        {
            string v;
            return strings.TryGetValue(key, out v) && v.Length > 0 ? v : en;
        }

        public static string F(string key, string en, params object[] args)
        {
            try {
                return string.Format(T(key, en), args);
            } catch (FormatException) {
                return string.Format(en, args);     // broken translation
            }
        }

        public static void Load(string path)
        {
            strings = new Dictionary<string, string>();
            if (path == null) return;
            try {
                foreach (var kv in Read(path, "strings")) strings[kv.Key] = kv.Value;
            } catch (Exception) { }
        }

        public static List<LangInfo> Available(string dir)
        {
            var list = new List<LangInfo>();
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*.ini")) {
                string name = Path.GetFileNameWithoutExtension(f);
                try {
                    foreach (var kv in Read(f, "language")) if (kv.Key == "name") name = kv.Value;
                } catch (Exception) { continue; }
                list.Add(new LangInfo { FilePath = f, Name = name });
            }
            list.Sort(delegate (LangInfo a, LangInfo b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        // key=value lines of one [section]; values are kept as written
        // (leading spaces matter), "\n" and "\t" are unescaped
        static IEnumerable<KeyValuePair<string, string>> Read(string path, string section)
        {
            string cur = "";
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8)) {
                string line = raw.TrimEnd('\r');
                string t = line.Trim();
                if (t.Length == 0 || t[0] == ';' || t[0] == '#') continue;
                if (t[0] == '[' && t.EndsWith("]")) { cur = t.Substring(1, t.Length - 2).Trim(); continue; }
                if (cur != section) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string v = line.Substring(eq + 1).Replace("\\n", "\n").Replace("\\t", "\t");
                yield return new KeyValuePair<string, string>(line.Substring(0, eq).Trim(), v);
            }
        }
    }
}
