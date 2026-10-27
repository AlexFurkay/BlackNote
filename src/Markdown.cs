using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using Markdig;
using Microsoft.Win32;
using ScintillaNET;

namespace BlackNote
{
    /// <summary>GitHub-flavoured Markdown editing commands. Every command is a single undo step.</summary>
    internal static class MarkdownCommands
    {
        private static readonly Regex HeadingRx = new Regex(@"^(#{1,6})[ \t]+");
        private static readonly Regex AnyListRx = new Regex(@"^([ \t]*)([-*+][ \t]+(\[[ xX]\][ \t]+)?|\d+[.)][ \t]+)");
        private static readonly Regex BulletRx = new Regex(@"^[ \t]*[-*+][ \t]+(?!\[[ xX]\])");
        private static readonly Regex NumberRx = new Regex(@"^[ \t]*\d+[.)][ \t]+");
        private static readonly Regex TaskRx = new Regex(@"^[ \t]*[-*+][ \t]+\[[ xX]\][ \t]+");
        private static readonly Regex QuoteRx = new Regex(@"^>[ \t]?");

        /// <summary>Wraps the selection in markers (**bold**), or removes them if already there.</summary>
        public static void Wrap(Scintilla sc, string left, string right, string placeholder)
        {
            int start = sc.SelectionStart, end = sc.SelectionEnd;
            string sel = sc.SelectedText;
            sc.BeginUndoAction();
            try
            {
                if (sel.Length == 0)
                {
                    sc.InsertText(start, left + placeholder + right);
                    sc.SetSelection(start + left.Length + placeholder.Length, start + left.Length);
                }
                else if (sel.Length >= left.Length + right.Length && sel.StartsWith(left, StringComparison.Ordinal) && sel.EndsWith(right, StringComparison.Ordinal))
                {
                    string inner = sel.Substring(left.Length, sel.Length - left.Length - right.Length);
                    sc.ReplaceSelection(inner);
                    sc.SetSelection(start + inner.Length, start);
                }
                else if (start >= left.Length && end + right.Length <= sc.TextLength
                         && sc.GetTextRange(start - left.Length, left.Length) == left
                         && sc.GetTextRange(end, right.Length) == right)
                {
                    sc.DeleteRange(end, right.Length);
                    sc.DeleteRange(start - left.Length, left.Length);
                    sc.SetSelection(end - left.Length, start - left.Length);
                }
                else
                {
                    sc.ReplaceSelection(left + sel + right);
                    sc.SetSelection(start + left.Length + sel.Length, start + left.Length);
                }
            }
            finally { sc.EndUndoAction(); }
        }

        public static void Heading(Scintilla sc, int level) => TransformLines(sc, lines =>
        {
            bool same = true;
            foreach (string l in lines)
            {
                var m = HeadingRx.Match(l);
                if (!m.Success || m.Groups[1].Length != level) { same = false; break; }
            }
            var result = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                string body = HeadingRx.Replace(lines[i], "", 1);
                result[i] = same ? body : new string('#', level) + " " + body;
            }
            return result;
        });

        public static void Bullets(Scintilla sc) => ToggleList(sc, BulletRx, _ => "- ");
        public static void Numbers(Scintilla sc) => ToggleList(sc, NumberRx, n => n + ". ");
        public static void Tasks(Scintilla sc) => ToggleList(sc, TaskRx, _ => "- [ ] ");

        public static void Quote(Scintilla sc) => TransformLines(sc, lines =>
        {
            bool all = Array.TrueForAll(lines, l => QuoteRx.IsMatch(l));
            return Array.ConvertAll(lines, l => all ? QuoteRx.Replace(l, "", 1) : "> " + l);
        });

        public static void CodeBlock(Scintilla sc)
        {
            string eol = TextCodec.EolString(sc.EolMode);
            if (sc.SelectionStart == sc.SelectionEnd)
            {
                int pos = sc.CurrentPosition;
                string pre = AtLineStart(sc, pos) ? "" : eol;
                sc.InsertText(pos, pre + "```" + eol + eol + "```" + eol);
                sc.SetEmptySelection(pos + pre.Length + 3 + eol.Length);
                return;
            }
            TransformLines(sc, lines =>
            {
                var r = new List<string> { "```" };
                r.AddRange(lines);
                r.Add("```");
                return r.ToArray();
            });
        }

        public static void Link(Scintilla sc, bool image)
        {
            string sel = sc.SelectedText;
            int start = sc.SelectionStart;
            string bang = image ? "!" : "";
            string label = image ? "описание" : "текст ссылки";
            sc.BeginUndoAction();
            try
            {
                if (sel.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || sel.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    sc.ReplaceSelection($"{bang}[{label}]({sel})");
                    sc.SetSelection(start + bang.Length + 1 + label.Length, start + bang.Length + 1);
                    return;
                }
                string text = sel.Length > 0 ? sel : label;
                const string url = "https://";
                sc.ReplaceSelection($"{bang}[{text}]({url})");
                int urlStart = start + bang.Length + text.Length + 3;
                if (sel.Length > 0) sc.SetSelection(urlStart + url.Length, urlStart);
                else sc.SetSelection(start + bang.Length + 1 + text.Length, start + bang.Length + 1);
            }
            finally { sc.EndUndoAction(); }
        }

        public static void Table(Scintilla sc) => InsertBlock(sc,
            "| Столбец 1 | Столбец 2 | Столбец 3 |\n| --- | --- | --- |\n|  |  |  |\n|  |  |  |\n", "Столбец 1");

        public static void Rule(Scintilla sc) => InsertBlock(sc, "\n---\n\n", null);

        public static void Details(Scintilla sc) => InsertBlock(sc,
            "<details>\n<summary>Заголовок</summary>\n\nСкрытый текст\n\n</details>\n", "Заголовок");

        private static void ToggleList(Scintilla sc, Regex own, Func<int, string> prefix) => TransformLines(sc, lines =>
        {
            bool multi = lines.Length > 1;
            bool all = true;
            foreach (string l in lines)
                if (!(multi && l.Trim().Length == 0) && !own.IsMatch(l)) { all = false; break; }

            var result = new string[lines.Length];
            int n = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (multi && l.Trim().Length == 0) { result[i] = l; continue; }
                var m = AnyListRx.Match(l);
                string indent = m.Success ? m.Groups[1].Value : "";
                string body = m.Success ? l.Substring(m.Length) : l;
                result[i] = all ? indent + body : indent + prefix(++n) + body;
            }
            return result;
        });

        /// <summary>Replaces the selected whole lines with transformed ones.</summary>
        private static void TransformLines(Scintilla sc, Func<string[], string[]> transform)
        {
            int first = sc.LineFromPosition(sc.SelectionStart);
            int last = sc.LineFromPosition(sc.SelectionEnd);
            if (last > first && sc.SelectionEnd == sc.Lines[last].Position) last--;

            var lines = new string[last - first + 1];
            for (int i = first; i <= last; i++) lines[i - first] = LineText(sc, i);
            int start = sc.Lines[first].Position;
            int end = sc.Lines[last].Position + lines[lines.Length - 1].Length;
            string result = string.Join(TextCodec.EolString(sc.EolMode), transform(lines));

            sc.BeginUndoAction();
            sc.TargetStart = start;
            sc.TargetEnd = end;
            sc.ReplaceTarget(result);
            sc.EndUndoAction();

            if (first == last) sc.SetEmptySelection(start + result.Length);
            else sc.SetSelection(start + result.Length, start);
        }

        private static void InsertBlock(Scintilla sc, string template, string select)
        {
            string eol = TextCodec.EolString(sc.EolMode);
            string text = template.Replace("\n", eol);
            int pos = sc.SelectionStart;
            if (!AtLineStart(sc, pos)) text = eol + text;
            sc.ReplaceSelection(text);
            if (select != null)
            {
                int i = text.IndexOf(select, StringComparison.Ordinal);
                if (i >= 0) sc.SetSelection(pos + i + select.Length, pos + i);
            }
        }

        private static bool AtLineStart(Scintilla sc, int pos) =>
            pos == 0 || sc.GetCharAt(pos - 1) == '\n' || sc.GetCharAt(pos - 1) == '\r';

        private static string LineText(Scintilla sc, int line) => sc.Lines[line].Text.TrimEnd('\r', '\n');

        public const string CheatSheet =
@"# Шпаргалка Markdown

Всё как на GitHub. Выдели текст и нажми
сочетание клавиш - или кнопку на панели.

## Текст
**жирный**              Ctrl+B
*курсив*                Ctrl+I
~~зачёркнутый~~         Ctrl+Shift+X
`код в строке`          Ctrl+`

## Заголовки
# Заголовок 1           Ctrl+1
## Заголовок 2          Ctrl+2
### Заголовок 3         Ctrl+3

## Списки
- пункт                 Ctrl+Shift+8
1. пункт                Ctrl+Shift+7
- [ ] задача            Ctrl+Shift+9
- [x] сделанная задача

## Блоки
> цитата                Ctrl+Shift+Q

```
блок кода               Ctrl+Shift+`
```

---                     Ctrl+Shift+H (линия)

## Ссылки и картинки
[текст](https://...)    Ctrl+K
![описание](https://)   Ctrl+Shift+K

## Таблица (Ctrl+Shift+T)
| Имя | Возраст |
| --- | ------- |
| Аня | 22      |

## Спойлер (Ctrl+Shift+D)
<details>
<summary>Нажми, чтобы открыть</summary>

Скрытый текст
</details>

## Мелочи
Пустая строка - новый абзац.
Два пробела в конце строки - перенос.
\*звёздочки\* - обратный слэш
отменяет форматирование.

# Редактор

## Выделение
Alt + мышь           - вертикальное
                       выделение: печатай
                       сразу во всех строках
Alt + Shift + стрелки - то же с клавиатуры
Ctrl + клик          - ещё один курсор

## Правка
Ctrl+Z / Ctrl+Y      - отменить / повторить
Ctrl+D               - дублировать строку
Ctrl+L               - вырезать строку
Ctrl+U / Ctrl+Shift+U - строчные / ПРОПИСНЫЕ

## Вид
Ctrl + колесо мыши   - масштаб
Ctrl+Shift+V         - предпросмотр
F1                   - эта шпаргалка
";
    }

    /// <summary>Renders Markdown to an HTML page styled like GitHub.</summary>
    internal static class MarkdownPreview
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        /// <summary>
        /// The WebBrowser control defaults to IE7 mode at 96 DPI; ask for IE11 rendering and
        /// screen-DPI scaling for this exe (per-user registry, no admin rights needed).
        /// </summary>
        public static void EnableModernBrowser()
        {
            SetFeature("FEATURE_BROWSER_EMULATION", 11001);
            SetFeature("FEATURE_96DPI_PIXEL", 1);
        }

        private static void SetFeature(string feature, int value)
        {
            try
            {
                string exe = Path.GetFileName(Environment.ProcessPath);
                using var key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Internet Explorer\Main\FeatureControl\" + feature);
                key?.SetValue(exe, value, RegistryValueKind.DWord);
            }
            catch { }
        }

        public static string ToHtml(string markdown, Color fore, Color back)
        {
            string body = Markdig.Markdown.ToHtml(markdown ?? "", Pipeline);
            bool light = Theme.IsLight(back);
            string css = Css
                .Replace("%BG%", Theme.Hex(back))
                .Replace("%FG%", Theme.Hex(fore))
                .Replace("%BORDER%", Theme.Hex(Theme.Blend(back, fore, 0.2f)))
                .Replace("%CODE%", Theme.Hex(Theme.Blend(back, fore, 0.08f)))
                .Replace("%MUTED%", Theme.Hex(Theme.Blend(back, fore, 0.62f)))
                .Replace("%THUMB%", Theme.Hex(Theme.Blend(back, fore, 0.25f)))
                .Replace("%LINK%", light ? "#0969da" : "#4493f8");
            return "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                   "<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><style>" + css +
                   "</style></head><body><div class=\"md\">" + body + "</div></body></html>";
        }

        private const string Css = @"
html { scrollbar-base-color: %BG%; scrollbar-face-color: %THUMB%; scrollbar-track-color: %BG%;
       scrollbar-arrow-color: %MUTED%; scrollbar-shadow-color: %BG%; scrollbar-highlight-color: %BG%; }
html, body { margin: 0; padding: 0; background: %BG%; color: %FG%; }
.md { font-family: 'Segoe UI', Helvetica, Arial, sans-serif; font-size: 15px; line-height: 1.5;
      padding: 16px 28px 40px; word-wrap: break-word; }
.md > *:first-child { margin-top: 0 !important; }
.md h1, .md h2, .md h3, .md h4, .md h5, .md h6 { margin: 24px 0 16px; font-weight: 600; line-height: 1.25; }
.md h1, .md h2 { padding-bottom: .3em; border-bottom: 1px solid %BORDER%; }
.md h1 { font-size: 2em; } .md h2 { font-size: 1.5em; } .md h3 { font-size: 1.25em; }
.md h4 { font-size: 1em; } .md h5 { font-size: .875em; } .md h6 { font-size: .85em; color: %MUTED%; }
.md p, .md ul, .md ol, .md blockquote, .md table, .md pre, .md details { margin: 0 0 16px; }
.md a { color: %LINK%; text-decoration: none; } .md a:hover { text-decoration: underline; }
.md code { font-family: Consolas, 'Courier New', monospace; font-size: 85%; background: %CODE%;
           padding: .2em .4em; border-radius: 6px; }
.md pre { background: %CODE%; padding: 16px; border-radius: 6px; overflow: auto; line-height: 1.45; }
.md pre code { background: transparent; padding: 0; font-size: 85%; }
.md blockquote { margin-left: 0; padding: 0 1em; color: %MUTED%; border-left: .25em solid %BORDER%; }
.md hr { height: .25em; padding: 0; margin: 24px 0; background: %BORDER%; border: 0; }
.md table { border-collapse: collapse; }
.md th, .md td { padding: 6px 13px; border: 1px solid %BORDER%; }
.md th { font-weight: 600; }
.md tr:nth-child(2n) { background: %CODE%; }
.md img { max-width: 100%; }
.md ul, .md ol { padding-left: 2em; }
.md li + li { margin-top: .25em; }
.md li.task-list-item { list-style-type: none; }
.md li.task-list-item input { margin: 0 .35em .25em -1.4em; vertical-align: middle; }
.md summary { cursor: pointer; }
.md del { color: %MUTED%; }
";
    }
}
