using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BlackNote
{
    /// <summary>
    /// Developer check: "BlackNote.exe --selftest &lt;folder&gt;" opens a window with sample Markdown,
    /// saves screenshots of the main window and the Replace dialog, and closes. User settings are not touched.
    /// </summary>
    internal static class SelfTest
    {
        private const string Sample =
@"# BlackNote

Тёмный **Блокнот** с *Markdown* как на `GitHub`.

## Списки
- первый пункт
- второй пункт
- [x] сделано
- [ ] не сделано

> Цитата: вертикальное выделение — Alt + мышь.

| Команда | Клавиши |
| ------- | ------- |
| Жирный  | Ctrl+B  |
| Ссылка  | Ctrl+K  |

```
код в блоке
```

[Ссылка на GitHub](https://github.com)
";

        public static void Run(string outDir)
        {
            Directory.CreateDirectory(outDir);
            Settings.TestMode = true;

            var form = new MainForm(null)
            {
                StartPosition = FormStartPosition.Manual,
                WindowState = FormWindowState.Normal,
                TopMost = true,
                Bounds = new Rectangle(40, 40, Theme.S(1400), Theme.S(820)),
            };
            var timer = new Timer { Interval = 2500 };
            int step = 0;

            form.Shown += (s, e) =>
            {
                form.Editor.Text = Sample;
                form.Editor.SetSavePoint();
                form.Editor.SetSelection(form.Editor.Text.IndexOf("Блокнот") + 7, form.Editor.Text.IndexOf("Блокнот"));
                timer.Start();
            };
            timer.Tick += (s, e) =>
            {
                step++;
                if (step == 1)
                {
                    Capture(form, Path.Combine(outDir, "plain.png"));
                    form.TestOpenPanels();
                }
                else if (step == 2)
                {
                    Capture(form, Path.Combine(outDir, "main.png"));
                    File.WriteAllText(Path.Combine(outDir, "diag.txt"), form.TestDiagnostics);
                    form.TestShowFind();
                }
                else
                {
                    timer.Stop();
                    File.WriteAllText(Path.Combine(outDir, "checks.txt"), RunChecks(form.Editor) + RunPinChecks(form));
                    var find = form.TestFindForm;
                    if (find != null)
                    {
                        Capture(find, Path.Combine(outDir, "replace.png"));
                        find.Close();
                    }
                    form.Editor.SetSavePoint();
                    form.Close();
                }
            };
            Application.Run(form);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        private static string RunPinChecks(MainForm form)
        {
            var log = new System.Text.StringBuilder();
            void Check(string name, string expected)
            {
                string actual = form.TestBarAfterHelp();
                log.AppendLine((actual == expected ? "OK   " : "FAIL ") + name + (actual == expected ? "" : $"  expected [{expected}] got [{actual}]"));
            }
            // the self-test opened the preview panel earlier
            Check("default: preview pinned and pressed", "Предпросмотр[on]");
            form.TestClickBar("preview");
            Check("click closes preview", "Предпросмотр");
            form.TestClickBar("preview");
            Check("click opens preview again", "Предпросмотр[on]");
            form.TestTogglePin("bold");
            form.TestTogglePin("h1");
            Check("pinned in click order", "Предпросмотр[on] | Жирный | H1");
            form.TestTogglePin("preview");
            Check("unpin", "Жирный | H1");
            form.TestTogglePin("preview");
            Check("re-pin goes last", "Жирный | H1 | Предпросмотр[on]");
            return log.ToString();
        }

        private static string RunChecks(ScintillaNET.Scintilla sc)
        {
            var log = new System.Text.StringBuilder();
            void Check(string name, string expected)
            {
                string actual = sc.Text;
                log.AppendLine((actual == expected ? "OK   " : "FAIL ") + name +
                               (actual == expected ? "" : $"  expected [{Esc(expected)}] got [{Esc(actual)}]"));
            }
            static string Esc(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n");
            void Reset(string text) { sc.Text = text; sc.EmptyUndoBuffer(); }

            Reset("a\r\nb\r\nc"); sc.SelectAll();
            MarkdownCommands.Bullets(sc); Check("bullets on", "- a\r\n- b\r\n- c");
            MarkdownCommands.Bullets(sc); Check("bullets off", "a\r\nb\r\nc");
            MarkdownCommands.Numbers(sc); Check("numbers", "1. a\r\n2. b\r\n3. c");
            MarkdownCommands.Tasks(sc); Check("numbers -> tasks", "- [ ] a\r\n- [ ] b\r\n- [ ] c");
            MarkdownCommands.Quote(sc); Check("quote", "> - [ ] a\r\n> - [ ] b\r\n> - [ ] c");
            sc.Undo(); Check("undo is one step", "- [ ] a\r\n- [ ] b\r\n- [ ] c");

            Reset("title"); sc.SetEmptySelection(2);
            MarkdownCommands.Heading(sc, 2); Check("heading 2", "## title");
            MarkdownCommands.Heading(sc, 1); Check("heading 2 -> 1", "# title");
            MarkdownCommands.Heading(sc, 1); Check("heading off", "title");

            Reset("one word two"); sc.SetSelection(8, 4);
            MarkdownCommands.Wrap(sc, "**", "**", "x"); Check("bold", "one **word** two");
            MarkdownCommands.Wrap(sc, "**", "**", "x"); Check("bold off", "one word two");
            sc.SetEmptySelection(0);
            MarkdownCommands.Wrap(sc, "`", "`", "код"); Check("code placeholder", "`код`one word two");

            Reset("x"); sc.SetSelection(1, 0);
            MarkdownCommands.Link(sc, false); Check("link", "[x](https://)");

            Reset("a\r\nb\r\nc");
            sc.RectangularSelectionAnchor = 0;
            sc.RectangularSelectionCaret = sc.Lines[2].Position;
            SendMessage(sc.Handle, 0x0102 /* WM_CHAR */, (IntPtr)' ', IntPtr.Zero);
            Check("Alt-column typing (3 lines)", " a\r\n b\r\n c");

            return log.ToString();
        }

        /// <summary>Renders the window itself (works even when other windows cover it).</summary>
        private static void Capture(Form f, string path)
        {
            Application.DoEvents();
            var b = f.Bounds;
            using var bmp = new Bitmap(b.Width, b.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(f.Handle, hdc, 2); // PW_RENDERFULLCONTENT
                g.ReleaseHdc(hdc);
            }
            bmp.Save(path);
        }
    }
}
