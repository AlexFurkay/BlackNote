using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using ScintillaNET;

namespace BlackNote
{
    /// <summary>User settings, stored as key=value lines in %APPDATA%\BlackNote\settings.ini.</summary>
    internal sealed class Settings
    {
        public static readonly Color DefaultText = Color.FromArgb(220, 220, 220);
        public static readonly Color DefaultBack = Color.FromArgb(30, 30, 30);

        /// <summary>Self-test: start with defaults and never write the settings file.</summary>
        public static bool TestMode;

        public string FontName = "Consolas";
        public float FontSize = 11f;
        public bool FontBold;
        public bool FontItalic;
        public bool WordWrap;
        public bool StatusBar = true;
        public bool LineNumbers;
        public bool MarkdownToolbar;
        public bool MarkdownHighlight = true;
        public bool CheatSheet;
        public bool Preview;
        public int CheatWidth;
        public int PreviewWidth;
        public int TextColor = DefaultText.ToArgb();
        public int BackColor = DefaultBack.ToArgb();
        public int[] CustomColors = Array.Empty<int>();
        public Rectangle Bounds;
        public bool Maximized;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlackNote", "settings.ini");

        private static IEnumerable<FieldInfo> Fields => typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance);

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (TestMode || !File.Exists(FilePath)) return s;
                var values = new Dictionary<string, string>();
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                foreach (var f in Fields)
                {
                    if (!values.TryGetValue(f.Name, out string v)) continue;
                    try { f.SetValue(s, Parse(f.FieldType, v)); } catch { }
                }
            }
            catch { }
            if (s.FontSize < 4 || s.FontSize > 200) s.FontSize = 11f;
            if (string.IsNullOrWhiteSpace(s.FontName)) s.FontName = "Consolas";
            return s;
        }

        public void Save()
        {
            if (TestMode) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var lines = new List<string>();
                foreach (var f in Fields) lines.Add(f.Name + "=" + Format(f.GetValue(this)));
                File.WriteAllLines(FilePath, lines, Encoding.UTF8);
            }
            catch { }
        }

        private static object Parse(Type t, string v)
        {
            var inv = CultureInfo.InvariantCulture;
            if (t == typeof(string)) return v;
            if (t == typeof(bool)) return bool.Parse(v);
            if (t == typeof(int)) return int.Parse(v, inv);
            if (t == typeof(float)) return float.Parse(v, inv);
            if (t == typeof(int[]))
                return v.Length == 0 ? Array.Empty<int>() : Array.ConvertAll(v.Split(','), p => int.Parse(p, inv));
            if (t == typeof(Rectangle))
            {
                var p = Array.ConvertAll(v.Split(','), x => int.Parse(x, inv));
                return new Rectangle(p[0], p[1], p[2], p[3]);
            }
            throw new NotSupportedException(t.Name);
        }

        private static string Format(object o) => o switch
        {
            float f => f.ToString(CultureInfo.InvariantCulture),
            int[] a => string.Join(",", a),
            Rectangle r => $"{r.X},{r.Y},{r.Width},{r.Height}",
            _ => Convert.ToString(o, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Encoding and line-ending detection, same rules as Windows Notepad.</summary>
    internal static class TextCodec
    {
        public static Encoding Ansi => Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);

        public static string Decode(byte[] b, out Encoding encoding, out string name)
        {
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            {
                encoding = new UTF8Encoding(true); name = "UTF-8 с BOM";
                return Encoding.UTF8.GetString(b, 3, b.Length - 3);
            }
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            {
                encoding = new UnicodeEncoding(false, true); name = "UTF-16 LE";
                return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            }
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            {
                encoding = new UnicodeEncoding(true, true); name = "UTF-16 BE";
                return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            }
            try
            {
                string s = new UTF8Encoding(false, true).GetString(b);
                encoding = new UTF8Encoding(false); name = "UTF-8";
                return s;
            }
            catch (DecoderFallbackException)
            {
                encoding = Ansi; name = "ANSI";
                return encoding.GetString(b);
            }
        }

        public static Eol DetectEol(string s)
        {
            int cr = s.IndexOf('\r'), lf = s.IndexOf('\n');
            if (cr < 0 && lf < 0) return Eol.CrLf;
            if (cr >= 0 && cr + 1 < s.Length && s[cr + 1] == '\n') return Eol.CrLf;
            if (cr >= 0 && (lf < 0 || cr < lf)) return Eol.Cr;
            return Eol.Lf;
        }

        public static string NormalizeEol(string s, Eol eol)
        {
            string n = s.Replace("\r\n", "\n").Replace('\r', '\n');
            return eol == Eol.Lf ? n : n.Replace("\n", eol == Eol.Cr ? "\r" : "\r\n");
        }

        public static string EolString(Eol eol) => eol == Eol.Lf ? "\n" : eol == Eol.Cr ? "\r" : "\r\n";

        public static string EolName(Eol eol) =>
            eol == Eol.Lf ? "Unix (LF)" : eol == Eol.Cr ? "Macintosh (CR)" : "Windows (CRLF)";

        public static bool CanEncode(Encoding e, string s) => e.GetString(e.GetBytes(s)) == s;
    }
}
