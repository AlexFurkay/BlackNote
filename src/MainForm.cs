using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ScintillaNET;

namespace BlackNote
{
    internal sealed class MainForm : Form, IMessageFilter
    {
        private const string AppName = "BlackNote";
        private const string FileFilter = "Текстовые документы (*.txt)|*.txt|Markdown (*.md)|*.md;*.markdown|Все файлы (*.*)|*.*";

        private readonly Settings cfg = T(Settings.Load(), "settings loaded");
        private readonly Scintilla editor = T(new Scintilla(), "scintilla #1");
        private readonly MenuStrip menu = new MenuStrip();
        private readonly ToolStrip mdBar = new ToolStrip();
        private readonly StatusStrip status = new StatusStrip();
        private readonly Panel cheatPanel = new Panel();
        private readonly Panel previewPanel = new Panel();
        private readonly Splitter cheatSplit = new Splitter();
        private readonly Splitter previewSplit = new Splitter();
        private readonly Scintilla cheatView = T(new Scintilla(), "scintilla #2");
        private readonly WebBrowser browser = T(new WebBrowser(), "web browser");

        private static TValue T<TValue>(TValue value, string step) { StartupTrace.Mark(step); return value; }
        private readonly Timer previewTimer = new Timer { Interval = 350 };

        private ToolStripStatusLabel lblPos, lblZoom, lblEol, lblEnc;
        private ToolStripMenuItem miWrap, miStatus, miNumbers, miMdBar, miCheat, miPreview, miHighlight;
        private ToolStripMenuItem miUndo, miRedo, miCut, miCopy, miPaste, miDelete, miFind, miFindNext, miFindPrev;
        private ToolStripButton btnCheat, btnPreview;

        private readonly string startupPath;
        private string filePath;
        private Encoding encoding = new UTF8Encoding(false);
        private string encodingName = "UTF-8";
        private bool formatDirty;     // encoding changed but text not
        private bool altMouseDown;    // Alt+drag selection: don't let releasing Alt open the menu
        private bool previewReady;
        private PageSettings pageSettings;
        private FindForm findForm;

        public string FindText = "";
        public string ReplaceText = "";
        public bool MatchCase;
        public bool WrapAround;
        public bool SearchUp;

        public MainForm(string path)
        {
            startupPath = path;
            Icon = new Icon(typeof(MainForm).Assembly.GetManifestResourceStream("BlackNote.BlackNote.ico") ?? Stream.Null);
            BackColor = Theme.Window;

            StartupTrace.Mark("form ctor");
            SetupEditor();
            StartupTrace.Mark("editor");
            SetupMenu();
            SetupMarkdownBar();
            SetupStatus();
            StartupTrace.Mark("menus");
            SetupSidePanels();
            StartupTrace.Mark("side panels");

            // Docking goes from the last added control to the first one.
            Controls.Add(editor);
            Controls.Add(previewSplit);
            Controls.Add(previewPanel);
            Controls.Add(cheatSplit);
            Controls.Add(cheatPanel);
            Controls.Add(status);
            Controls.Add(mdBar);
            Controls.Add(menu);
            MainMenuStrip = menu;

            ApplySettings();
            StartupTrace.Mark("settings applied");
            Application.AddMessageFilter(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartupTrace.Mark("shown");
        }

        // ───────────────────────────── setup ─────────────────────────────

        private void SetupEditor()
        {
            editor.Dock = DockStyle.Fill;
            editor.BorderStyle = ScintillaNET.BorderStyle.None;
            editor.Technology = Technology.DirectWrite;
            editor.MultipleSelection = true;            // Ctrl+click = extra caret
            editor.AdditionalSelectionTyping = true;    // type into every caret / column at once
            editor.MultiPaste = MultiPaste.Each;
            editor.VirtualSpaceOptions = VirtualSpace.RectangularSelection;
            editor.MouseSelectionRectangularSwitch = true;
            editor.PasteConvertEndings = true;
            editor.ScrollWidthTracking = true;
            editor.ScrollWidth = 1;
            editor.CaretWidth = Math.Max(1, Theme.S(1));
            editor.EolMode = Eol.CrLf;
            for (int i = 0; i < 5; i++) editor.Margins[i].Width = 0;
            editor.Margins.Left = Theme.S(4);
            editor.Margins.Right = Theme.S(4);
            editor.UsePopup(PopupMode.Never);
            editor.ContextMenuStrip = BuildContextMenu();
            editor.AssignCmdKey(Keys.Control | Keys.Shift | Keys.Z, Command.Redo);

            editor.SavePointLeft += (s, e) => UpdateTitle();
            editor.SavePointReached += (s, e) => UpdateTitle();
            editor.UpdateUI += (s, e) =>
            {
                if ((e.Change & (UpdateChange.Selection | UpdateChange.Content)) != 0) UpdatePosition();
                if ((e.Change & UpdateChange.VScroll) != 0) SyncPreviewScroll();
            };
            editor.ZoomChanged += (s, e) => UpdateZoomLabel();
            editor.Insert += (s, e) => OnContentChanged(e.LinesAdded);
            editor.Delete += (s, e) => OnContentChanged(e.LinesAdded);
            editor.MouseDown += (s, e) => { if ((ModifierKeys & Keys.Alt) != 0) altMouseDown = true; };
            editor.HandleCreated += (s, e) => Theme.DarkScrollBars(editor.Handle);
        }

        private void SetupMenu()
        {
            menu.BackColor = Theme.Menu;
            menu.ForeColor = Theme.Text;
            menu.Padding = new Padding(Theme.S(2), Theme.S(2), 0, Theme.S(2));

            var file = Root("&Файл",
                Item("&Создать", (s, e) => NewDocument(), Keys.Control | Keys.N),
                Item("&Новое окно", (s, e) => NewWindow(), Keys.Control | Keys.Shift | Keys.N),
                Item("&Открыть...", (s, e) => OpenDocument(), Keys.Control | Keys.O),
                Item("Со&хранить", (s, e) => Save(), Keys.Control | Keys.S),
                Item("Сохранить &как...", (s, e) => SaveAs(), Keys.Control | Keys.Shift | Keys.S),
                Sep(),
                BuildEncodingMenu(),
                BuildEolMenu(),
                Sep(),
                Item("Пара&метры страницы...", (s, e) => PageSetup()),
                Item("&Печать...", (s, e) => Print(), Keys.Control | Keys.P),
                Sep(),
                Item("В&ыход", (s, e) => Close()));

            var edit = Root("&Правка",
                miUndo = Item("&Отменить", (s, e) => Undo(), Keys.Control | Keys.Z),
                miRedo = Item("&Повторить", (s, e) => Redo(), Keys.Control | Keys.Y),
                Sep(),
                miCut = Item("&Вырезать", (s, e) => Cut(), Keys.Control | Keys.X),
                miCopy = Item("&Копировать", (s, e) => Copy(), Keys.Control | Keys.C),
                miPaste = Item("Вст&авить", (s, e) => Paste(), Keys.Control | Keys.V),
                miDelete = Item("&Удалить", (s, e) => editor.Clear(), Keys.None, "Del"),
                Sep(),
                miFind = Item("&Найти...", (s, e) => ShowFind(false), Keys.Control | Keys.F),
                miFindNext = Item("Найти &далее", (s, e) => FindNext(false), Keys.F3),
                miFindPrev = Item("Найти &ранее", (s, e) => FindNext(true), Keys.Shift | Keys.F3),
                Item("&Заменить...", (s, e) => ShowFind(true), Keys.Control | Keys.H),
                Item("Пе&рейти...", (s, e) => GoToLine(), Keys.Control | Keys.G),
                Sep(),
                Item("Выделить в&се", (s, e) => SelectAll(), Keys.Control | Keys.A),
                Item("Вр&емя и дата", (s, e) => editor.ReplaceSelection(DateTime.Now.ToString("HH:mm dd.MM.yyyy")), Keys.F5));
            edit.DropDownOpening += (s, e) =>
            {
                bool sel = editor.SelectionStart != editor.SelectionEnd;
                bool has = editor.TextLength > 0;
                miUndo.Enabled = editor.CanUndo;
                miRedo.Enabled = editor.CanRedo;
                miCut.Enabled = miCopy.Enabled = miDelete.Enabled = sel;
                miPaste.Enabled = editor.CanPaste;
                miFind.Enabled = miFindNext.Enabled = miFindPrev.Enabled = has;
            };
            // disabled items would swallow their shortcut keys
            edit.DropDownClosed += (s, e) => { foreach (ToolStripItem i in edit.DropDownItems) i.Enabled = true; };

            var format = Root("Фор&мат",
                miWrap = Item("&Перенос по словам", (s, e) => SetWordWrap(!cfg.WordWrap)),
                Item("&Шрифт...", (s, e) => ChooseFont()));

            // "Советы": everything about Markdown formatting (GitHub style) in one place
            var tips = Root("Со&веты",
                miCheat = Item("&Шпаргалка: как форматировать", (s, e) => SetCheatSheet(!cfg.CheatSheet), Keys.F1),
                miPreview = Item("Пред&просмотр (как на GitHub)", (s, e) => SetPreview(!cfg.Preview), Keys.Control | Keys.Shift | Keys.V),
                miMdBar = Item("П&анель кнопок разметки", (s, e) => SetMarkdownBar(!cfg.MarkdownToolbar)),
                miHighlight = Item("Под&светка разметки", (s, e) => SetHighlight(!cfg.MarkdownHighlight)),
                Sep(),
                Item("&Жирный", (s, e) => MarkdownCommands.Wrap(editor, "**", "**", "жирный текст"), Keys.Control | Keys.B),
                Item("&Курсив", (s, e) => MarkdownCommands.Wrap(editor, "*", "*", "курсив"), Keys.Control | Keys.I),
                Item("&Зачёркнутый", (s, e) => MarkdownCommands.Wrap(editor, "~~", "~~", "зачёркнутый"), Keys.Control | Keys.Shift | Keys.X),
                Item("Код в &строке", (s, e) => MarkdownCommands.Wrap(editor, "`", "`", "код"), Keys.Control | Keys.Oemtilde, "Ctrl+`"),
                Sep(),
                Item("Заголовок &1", (s, e) => MarkdownCommands.Heading(editor, 1), Keys.Control | Keys.D1),
                Item("Заголовок &2", (s, e) => MarkdownCommands.Heading(editor, 2), Keys.Control | Keys.D2),
                Item("Заголовок &3", (s, e) => MarkdownCommands.Heading(editor, 3), Keys.Control | Keys.D3),
                Sep(),
                Item("&Маркированный список", (s, e) => MarkdownCommands.Bullets(editor), Keys.Control | Keys.Shift | Keys.D8),
                Item("&Нумерованный список", (s, e) => MarkdownCommands.Numbers(editor), Keys.Control | Keys.Shift | Keys.D7),
                Item("Список &задач", (s, e) => MarkdownCommands.Tasks(editor), Keys.Control | Keys.Shift | Keys.D9),
                Item("&Цитата", (s, e) => MarkdownCommands.Quote(editor), Keys.Control | Keys.Shift | Keys.Q),
                Item("&Блок кода", (s, e) => MarkdownCommands.CodeBlock(editor), Keys.Control | Keys.Shift | Keys.Oemtilde, "Ctrl+Shift+`"),
                Sep(),
                Item("&Ссылка", (s, e) => MarkdownCommands.Link(editor, false), Keys.Control | Keys.K),
                Item("&Картинка", (s, e) => MarkdownCommands.Link(editor, true), Keys.Control | Keys.Shift | Keys.K),
                Item("&Таблица", (s, e) => MarkdownCommands.Table(editor), Keys.Control | Keys.Shift | Keys.T),
                Item("&Линия", (s, e) => MarkdownCommands.Rule(editor), Keys.Control | Keys.Shift | Keys.H),
                Item("С&пойлер", (s, e) => MarkdownCommands.Details(editor), Keys.Control | Keys.Shift | Keys.D));

            var colors = Root("&Цвета",
                Item("Цвет &текста...", (s, e) => PickColor(true)),
                Item("Цвет &фона...", (s, e) => PickColor(false)),
                Root("&Готовые схемы",
                    Preset("Тёмная (по умолчанию)", Settings.DefaultText, Settings.DefaultBack),
                    Preset("Чёрная", Color.FromArgb(212, 212, 212), Color.Black),
                    Preset("Графит", Color.FromArgb(230, 230, 230), Color.FromArgb(43, 45, 48)),
                    Preset("Тёмная сепия", Color.FromArgb(232, 220, 196), Color.FromArgb(38, 35, 32)),
                    Preset("Ночная синяя", Color.FromArgb(204, 214, 232), Color.FromArgb(22, 27, 38)),
                    Preset("Зелёный терминал", Color.FromArgb(70, 230, 120), Color.FromArgb(12, 12, 12)),
                    Preset("Светлая", Color.FromArgb(30, 30, 30), Color.White)),
                Sep(),
                Item("&Сбросить цвета", (s, e) => SetColors(Settings.DefaultText, Settings.DefaultBack)));

            var view = Root("&Вид",
                Root("&Масштаб",
                    Item("&Увеличить", (s, e) => editor.ZoomIn(), Keys.Control | Keys.Oemplus, "Ctrl+плюс"),
                    Item("У&меньшить", (s, e) => editor.ZoomOut(), Keys.Control | Keys.OemMinus, "Ctrl+минус"),
                    Item("&Восстановить масштаб по умолчанию", (s, e) => editor.Zoom = 0, Keys.Control | Keys.D0)),
                miStatus = Item("&Строка состояния", (s, e) => SetStatusBar(!cfg.StatusBar)),
                miNumbers = Item("&Номера строк", (s, e) => SetLineNumbers(!cfg.LineNumbers)));

            var help = Root("&Справка",
                Item("Просмотреть &справку", (s, e) => SetCheatSheet(true)),
                Sep(),
                Item("&О программе BlackNote", (s, e) => About()));

            menu.Items.AddRange(new ToolStripItem[] { file, edit, format, view, tips, colors, help });
        }

        private ToolStripMenuItem BuildEncodingMenu()
        {
            var root = new ToolStripMenuItem("&Кодировка");
            (string Name, Func<Encoding> Make)[] list =
            {
                ("UTF-8", () => new UTF8Encoding(false)),
                ("UTF-8 с BOM", () => new UTF8Encoding(true)),
                ("UTF-16 LE", () => new UnicodeEncoding(false, true)),
                ("UTF-16 BE", () => new UnicodeEncoding(true, true)),
                ("ANSI", () => TextCodec.Ansi),
            };
            foreach (var (name, make) in list)
            {
                var mi = new ToolStripMenuItem(name) { Tag = name };
                mi.Click += (s, e) =>
                {
                    if (encodingName == name) return;
                    encoding = make();
                    encodingName = name;
                    formatDirty = true;
                    UpdateTitle();
                    UpdateStatusLabels();
                };
                root.DropDownItems.Add(mi);
            }
            root.DropDownOpening += (s, e) =>
            {
                foreach (ToolStripMenuItem mi in root.DropDownItems) mi.Checked = (string)mi.Tag == encodingName;
            };
            return root;
        }

        private ToolStripMenuItem BuildEolMenu()
        {
            var root = new ToolStripMenuItem("Конец &строки");
            foreach (var eol in new[] { Eol.CrLf, Eol.Lf, Eol.Cr })
            {
                var mi = new ToolStripMenuItem(TextCodec.EolName(eol)) { Tag = eol };
                mi.Click += (s, e) =>
                {
                    if (editor.EolMode == eol) return;
                    editor.ConvertEols(eol);
                    editor.EolMode = eol;
                    UpdateStatusLabels();
                };
                root.DropDownItems.Add(mi);
            }
            root.DropDownOpening += (s, e) =>
            {
                foreach (ToolStripMenuItem mi in root.DropDownItems) mi.Checked = (Eol)mi.Tag == editor.EolMode;
            };
            return root;
        }

        private void SetupMarkdownBar()
        {
            mdBar.GripStyle = ToolStripGripStyle.Hidden;
            mdBar.BackColor = Theme.Menu;
            mdBar.ForeColor = Theme.Text;
            mdBar.Padding = new Padding(Theme.S(4), Theme.S(1), Theme.S(4), Theme.S(3));
            mdBar.ShowItemToolTips = true;

            var baseFont = mdBar.Font;
            mdBar.Items.AddRange(new ToolStripItem[]
            {
                MdButton("B", "Жирный (Ctrl+B)", () => MarkdownCommands.Wrap(editor, "**", "**", "жирный текст"), new Font(baseFont, FontStyle.Bold)),
                MdButton("I", "Курсив (Ctrl+I)", () => MarkdownCommands.Wrap(editor, "*", "*", "курсив"), new Font(baseFont, FontStyle.Italic)),
                MdButton("S", "Зачёркнутый (Ctrl+Shift+X)", () => MarkdownCommands.Wrap(editor, "~~", "~~", "зачёркнутый"), new Font(baseFont, FontStyle.Strikeout)),
                MdButton("</>", "Код в строке (Ctrl+`)", () => MarkdownCommands.Wrap(editor, "`", "`", "код")),
                new ToolStripSeparator(),
                MdButton("H1", "Заголовок 1 (Ctrl+1)", () => MarkdownCommands.Heading(editor, 1)),
                MdButton("H2", "Заголовок 2 (Ctrl+2)", () => MarkdownCommands.Heading(editor, 2)),
                MdButton("H3", "Заголовок 3 (Ctrl+3)", () => MarkdownCommands.Heading(editor, 3)),
                new ToolStripSeparator(),
                MdButton("• Список", "Маркированный список (Ctrl+Shift+8)", () => MarkdownCommands.Bullets(editor)),
                MdButton("1. Список", "Нумерованный список (Ctrl+Shift+7)", () => MarkdownCommands.Numbers(editor)),
                MdButton("☐ Задачи", "Список задач (Ctrl+Shift+9)", () => MarkdownCommands.Tasks(editor)),
                MdButton("❝ Цитата", "Цитата (Ctrl+Shift+Q)", () => MarkdownCommands.Quote(editor)),
                MdButton("{ } Блок кода", "Блок кода (Ctrl+Shift+`)", () => MarkdownCommands.CodeBlock(editor)),
                new ToolStripSeparator(),
                MdButton("Ссылка", "Ссылка (Ctrl+K)", () => MarkdownCommands.Link(editor, false)),
                MdButton("Картинка", "Картинка (Ctrl+Shift+K)", () => MarkdownCommands.Link(editor, true)),
                MdButton("Таблица", "Таблица (Ctrl+Shift+T)", () => MarkdownCommands.Table(editor)),
                MdButton("Линия", "Горизонтальная линия (Ctrl+Shift+H)", () => MarkdownCommands.Rule(editor)),
                MdButton("Спойлер", "Спойлер (Ctrl+Shift+D)", () => MarkdownCommands.Details(editor)),
            });

            btnPreview = MdButton("Предпросмотр", "Как будет выглядеть на GitHub (Ctrl+Shift+V)", () => SetPreview(!cfg.Preview));
            btnCheat = MdButton("Шпаргалка", "Все команды и разметка Markdown (F1)", () => SetCheatSheet(!cfg.CheatSheet));
            btnPreview.Alignment = ToolStripItemAlignment.Right;
            btnCheat.Alignment = ToolStripItemAlignment.Right;
            mdBar.Items.Add(btnPreview);
            mdBar.Items.Add(btnCheat);
        }

        private ToolStripButton MdButton(string text, string tip, Action action, Font font = null)
        {
            var b = new ToolStripButton(text)
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = tip,
                AutoToolTip = false,
                Padding = new Padding(Theme.S(4), Theme.S(1), Theme.S(4), Theme.S(1)),
            };
            if (font != null) b.Font = font;
            b.Click += (s, e) => { action(); if (b != btnCheat && b != btnPreview) editor.Focus(); };
            return b;
        }

        private void SetupStatus()
        {
            status.BackColor = Theme.Status;
            status.ForeColor = Theme.Text;
            status.SizingGrip = true;
            status.AutoSize = false; // StatusStrip keeps its 96-DPI height on scaled screens
            status.Height = status.Font.Height + Theme.S(10);
            status.Items.Add(new ToolStripStatusLabel { Spring = true });
            lblPos = Segment(160);
            lblZoom = Segment(64);
            lblEol = Segment(136);
            lblEnc = Segment(120);
            status.Items.AddRange(new ToolStripItem[] { lblPos, lblZoom, lblEol, lblEnc });
        }

        private static ToolStripStatusLabel Segment(int width) => new ToolStripStatusLabel
        {
            AutoSize = false,
            Size = new Size(Theme.S(width), Theme.S(22)),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(Theme.S(8), 0, 0, 0),
            Tag = "segment",
        };

        private void SetupSidePanels()
        {
            cheatPanel.Dock = DockStyle.Right;
            cheatPanel.Width = cfg.CheatWidth > Theme.S(150) ? cfg.CheatWidth : Theme.S(400);
            cheatPanel.BackColor = Theme.Window;
            cheatSplit.Dock = DockStyle.Right;
            cheatSplit.Width = Theme.S(4);
            cheatSplit.BackColor = Theme.Border;
            cheatSplit.MinSize = Theme.S(150);

            cheatView.Dock = DockStyle.Fill;
            cheatView.BorderStyle = ScintillaNET.BorderStyle.None;
            cheatView.Technology = Technology.DirectWrite;
            for (int i = 0; i < 5; i++) cheatView.Margins[i].Width = 0;
            cheatView.Margins.Left = Theme.S(10);
            cheatView.UsePopup(PopupMode.Text);
            cheatView.Text = MarkdownCommands.CheatSheet;
            cheatView.ReadOnly = true;
            cheatView.EmptyUndoBuffer();
            cheatView.HandleCreated += (s, e) => Theme.DarkScrollBars(cheatView.Handle);
            cheatPanel.Controls.Add(cheatView);
            cheatPanel.Controls.Add(SideHeader("Шпаргалка Markdown", () => SetCheatSheet(false)));

            previewPanel.Dock = DockStyle.Right;
            previewPanel.Width = cfg.PreviewWidth > Theme.S(150) ? cfg.PreviewWidth : Theme.S(520);
            previewPanel.BackColor = Theme.Window;
            previewSplit.Dock = DockStyle.Right;
            previewSplit.Width = Theme.S(4);
            previewSplit.BackColor = Theme.Border;
            previewSplit.MinSize = Theme.S(150);

            browser.Dock = DockStyle.Fill;
            browser.ScriptErrorsSuppressed = true;
            browser.IsWebBrowserContextMenuEnabled = false;
            browser.AllowWebBrowserDrop = false;
            browser.Navigating += (s, e) =>
            {
                if (e.Url == null || e.Url.Scheme == "about") return; // our own page
                e.Cancel = true;
                if (e.Url.Scheme == "http" || e.Url.Scheme == "https" || e.Url.Scheme == "mailto")
                {
                    try { Process.Start(new ProcessStartInfo(e.Url.ToString()) { UseShellExecute = true }); } catch { }
                }
            };
            browser.DocumentCompleted += (s, e) => { previewReady = true; SyncPreviewScroll(); };
            previewPanel.Controls.Add(browser);
            previewPanel.Controls.Add(SideHeader("Предпросмотр - как на GitHub", () => SetPreview(false)));

            previewTimer.Tick += (s, e) => { previewTimer.Stop(); RenderPreview(); };
        }

        private static Panel SideHeader(string title, Action close)
        {
            var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(28), BackColor = Theme.Menu };
            var x = new Label
            {
                Text = "✕",
                Dock = DockStyle.Right,
                Width = Theme.S(30),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Text,
                Cursor = Cursors.Hand,
            };
            x.MouseEnter += (s, e) => x.BackColor = Theme.Hover;
            x.MouseLeave += (s, e) => x.BackColor = Theme.Menu;
            x.Click += (s, e) => close();
            var label = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Text,
                Padding = new Padding(Theme.S(8), 0, 0, 0),
            };
            header.Controls.Add(label);
            header.Controls.Add(x);
            return header;
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var cm = new ContextMenuStrip();
            ToolStripMenuItem undo, redo, cut, copy, paste, del;
            cm.Items.AddRange(new ToolStripItem[]
            {
                undo = Item("Отменить", (s, e) => editor.Undo(), Keys.None, "Ctrl+Z"),
                redo = Item("Повторить", (s, e) => editor.Redo(), Keys.None, "Ctrl+Y"),
                Sep(),
                cut = Item("Вырезать", (s, e) => editor.Cut(), Keys.None, "Ctrl+X"),
                copy = Item("Копировать", (s, e) => editor.Copy(), Keys.None, "Ctrl+C"),
                paste = Item("Вставить", (s, e) => editor.Paste(), Keys.None, "Ctrl+V"),
                del = Item("Удалить", (s, e) => editor.Clear(), Keys.None, "Del"),
                Sep(),
                Item("Выделить все", (s, e) => editor.SelectAll(), Keys.None, "Ctrl+A"),
                Sep(),
                Item("Жирный", (s, e) => MarkdownCommands.Wrap(editor, "**", "**", "жирный текст"), Keys.None, "Ctrl+B"),
                Item("Курсив", (s, e) => MarkdownCommands.Wrap(editor, "*", "*", "курсив"), Keys.None, "Ctrl+I"),
                Item("Код", (s, e) => MarkdownCommands.Wrap(editor, "`", "`", "код"), Keys.None, "Ctrl+`"),
                Item("Ссылка", (s, e) => MarkdownCommands.Link(editor, false), Keys.None, "Ctrl+K"),
            });
            cm.Opening += (s, e) =>
            {
                bool sel = editor.SelectionStart != editor.SelectionEnd;
                undo.Enabled = editor.CanUndo;
                redo.Enabled = editor.CanRedo;
                cut.Enabled = copy.Enabled = del.Enabled = sel;
                paste.Enabled = editor.CanPaste;
            };
            return cm;
        }

        private static ToolStripMenuItem Item(string text, EventHandler onClick, Keys keys = Keys.None, string keyText = null)
        {
            var mi = new ToolStripMenuItem(text, null, onClick);
            if (keys != Keys.None) mi.ShortcutKeys = keys;
            if (keyText != null) mi.ShortcutKeyDisplayString = keyText;
            return mi;
        }

        private static ToolStripMenuItem Root(string text, params ToolStripItem[] items)
        {
            var m = new ToolStripMenuItem(text);
            m.DropDownItems.AddRange(items);
            return m;
        }

        private static ToolStripSeparator Sep() => new ToolStripSeparator();

        private ToolStripMenuItem Preset(string name, Color fore, Color back) => Item(name, (s, e) => SetColors(fore, back));

        // ──────────────────────────── settings ────────────────────────────

        private void ApplySettings()
        {
            if (cfg.Bounds.Width > 200 && cfg.Bounds.Height > 150 && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(cfg.Bounds)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = cfg.Bounds;
            }
            else
            {
                Size = new Size(Theme.S(1040), Theme.S(680));
                StartPosition = FormStartPosition.WindowsDefaultLocation;
            }
            if (cfg.Maximized) WindowState = FormWindowState.Maximized;

            ApplyEditorStyles();
            SetWordWrap(cfg.WordWrap);
            SetStatusBar(cfg.StatusBar);
            SetLineNumbers(cfg.LineNumbers);
            SetMarkdownBar(cfg.MarkdownToolbar);
            SetCheatSheet(cfg.CheatSheet);
            SetPreview(cfg.Preview);
            miHighlight.Checked = cfg.MarkdownHighlight;
            UpdateTitle();
            UpdateStatusLabels();
        }

        private Color TextColor => Color.FromArgb(255, Color.FromArgb(cfg.TextColor));
        private Color BackgroundColor => Color.FromArgb(255, Color.FromArgb(cfg.BackColor));

        private void ApplyEditorStyles()
        {
            StyleScintilla(editor, cfg.MarkdownHighlight);
            StyleScintilla(cheatView, true);
            UpdateLineNumberMargin();
            UpdateZoomLabel();
            if (cfg.Preview) RenderPreview();
        }

        private void StyleScintilla(Scintilla sc, bool markdown)
        {
            Color fg = TextColor, bg = BackgroundColor;
            bool light = Theme.IsLight(bg);

            var d = sc.Styles[Style.Default];
            d.Font = cfg.FontName;
            d.SizeF = cfg.FontSize;
            d.Bold = cfg.FontBold;
            d.Italic = cfg.FontItalic;
            d.ForeColor = fg;
            d.BackColor = bg;
            sc.StyleClearAll();

            sc.Styles[Style.LineNumber].ForeColor = Theme.Blend(bg, fg, 0.45f);
            sc.Styles[Style.LineNumber].BackColor = Theme.Blend(bg, fg, 0.05f);
            sc.CaretForeColor = fg;
            sc.AdditionalCaretForeColor = fg;
            Color selection = light ? Color.FromArgb(173, 214, 255) : Color.FromArgb(38, 79, 120);
            sc.SelectionBackColor = selection;

            if (!markdown)
            {
                SetLexer(sc, "null");
                return;
            }
            SetLexer(sc, "markdown");
            Color heading = light ? Color.FromArgb(0, 92, 197) : Color.FromArgb(86, 182, 255);
            Color link = light ? Color.FromArgb(9, 105, 218) : Color.FromArgb(68, 147, 248);
            Color code = light ? Color.FromArgb(163, 21, 21) : Color.FromArgb(214, 157, 133);
            Color quote = light ? Color.FromArgb(0, 128, 0) : Color.FromArgb(118, 170, 96);
            Color marker = light ? Color.FromArgb(175, 0, 219) : Color.FromArgb(205, 140, 210);
            Color dim = Theme.Blend(bg, fg, 0.5f);
            Color codeBack = Theme.Blend(bg, fg, 0.07f);

            void Set(int style, Color color, bool bold = false, bool italic = false, bool underline = false, Color? back = null)
            {
                var st = sc.Styles[style];
                st.ForeColor = color;
                st.Bold = bold || cfg.FontBold;
                st.Italic = italic || cfg.FontItalic;
                st.Underline = underline;
                if (back.HasValue) st.BackColor = back.Value;
            }

            Set(Style.Markdown.Default, fg);
            Set(Style.Markdown.LineBegin, fg);
            Set(Style.Markdown.Strong1, fg, bold: true);
            Set(Style.Markdown.Strong2, fg, bold: true);
            Set(Style.Markdown.Em1, fg, italic: true);
            Set(Style.Markdown.Em2, fg, italic: true);
            Set(Style.Markdown.Header1, heading, bold: true);
            Set(Style.Markdown.Header2, heading, bold: true);
            Set(Style.Markdown.Header3, heading, bold: true);
            Set(Style.Markdown.Header4, heading, bold: true);
            Set(Style.Markdown.Header5, heading, bold: true);
            Set(Style.Markdown.Header6, heading, bold: true);
            Set(Style.Markdown.UListItem, marker, bold: true);
            Set(Style.Markdown.OListItem, marker, bold: true);
            Set(Style.Markdown.BlockQuote, quote);
            Set(Style.Markdown.Strikeout, dim);
            Set(Style.Markdown.HRule, dim);
            Set(Style.Markdown.Link, link, underline: true);
            Set(Style.Markdown.Code, code, back: codeBack);
            Set(Style.Markdown.Code2, code, back: codeBack);
            Set(Style.Markdown.CodeBk, code, back: codeBack);
            Set(Style.Markdown.PreChar, code);
            sc.Styles[Style.Markdown.CodeBk].FillLine = true;
        }

        private static void SetLexer(Scintilla sc, string name)
        {
            try { sc.LexerName = name; } catch { }
        }

        private void SetWordWrap(bool on)
        {
            cfg.WordWrap = on;
            editor.WrapMode = on ? WrapMode.Word : WrapMode.None;
            miWrap.Checked = on;
        }

        private void SetStatusBar(bool on)
        {
            cfg.StatusBar = on;
            status.Visible = on;
            miStatus.Checked = on;
        }

        private void SetLineNumbers(bool on)
        {
            cfg.LineNumbers = on;
            miNumbers.Checked = on;
            UpdateLineNumberMargin();
        }

        private void SetMarkdownBar(bool on)
        {
            cfg.MarkdownToolbar = on;
            mdBar.Visible = on;
            miMdBar.Checked = on;
        }

        private void SetCheatSheet(bool on)
        {
            cfg.CheatSheet = on;
            cheatPanel.Visible = cheatSplit.Visible = on;
            miCheat.Checked = btnCheat.Checked = on;
        }

        private void SetPreview(bool on)
        {
            cfg.Preview = on;
            previewPanel.Visible = previewSplit.Visible = on;
            miPreview.Checked = btnPreview.Checked = on;
            if (on) RenderPreview();
        }

        private void SetHighlight(bool on)
        {
            cfg.MarkdownHighlight = on;
            miHighlight.Checked = on;
            StyleScintilla(editor, on);
        }

        private void SetColors(Color fore, Color back)
        {
            cfg.TextColor = fore.ToArgb();
            cfg.BackColor = back.ToArgb();
            ApplyEditorStyles();
            cfg.Save();
        }

        private void PickColor(bool text)
        {
            using var dlg = new ColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                Color = text ? TextColor : BackgroundColor,
                CustomColors = cfg.CustomColors,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            cfg.CustomColors = dlg.CustomColors;
            if (text) SetColors(dlg.Color, BackgroundColor);
            else SetColors(TextColor, dlg.Color);
        }

        private void ChooseFont()
        {
            var style = (cfg.FontBold ? FontStyle.Bold : 0) | (cfg.FontItalic ? FontStyle.Italic : 0);
            using var current = new Font(cfg.FontName, cfg.FontSize, style);
            using var dlg = new FontDialog { Font = current, ShowEffects = false, FontMustExist = true, AllowVerticalFonts = false };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            cfg.FontName = dlg.Font.Name;
            cfg.FontSize = dlg.Font.SizeInPoints;
            cfg.FontBold = dlg.Font.Bold;
            cfg.FontItalic = dlg.Font.Italic;
            ApplyEditorStyles();
            cfg.Save();
        }

        // ───────────────────────────── status ─────────────────────────────

        private bool IsDirty => editor.Modified || formatDirty;
        private string DocName => filePath == null ? "Безымянный" : Path.GetFileName(filePath);

        private void UpdateTitle() => Text = (IsDirty ? "*" : "") + DocName + " – " + AppName;

        private void UpdatePosition()
        {
            int pos = editor.CurrentPosition;
            int line = editor.LineFromPosition(pos);
            int col = editor.GetColumn(pos);
            int carets = editor.Selections.Count;
            lblPos.Text = $"Стр {line + 1}, стлб {col + 1}" + (carets > 1 ? $"  ({carets} кур.)" : "");
        }

        private void UpdateZoomLabel()
        {
            float size = cfg.FontSize;
            lblZoom.Text = $"{Math.Round((size + editor.Zoom) / size * 100)}%";
        }

        private void UpdateStatusLabels()
        {
            UpdatePosition();
            UpdateZoomLabel();
            lblEol.Text = TextCodec.EolName(editor.EolMode);
            lblEnc.Text = encodingName;
        }

        private void UpdateLineNumberMargin()
        {
            var m = editor.Margins[0];
            if (!cfg.LineNumbers) { m.Width = 0; return; }
            m.Type = MarginType.Number;
            int digits = Math.Max(3, editor.Lines.Count.ToString().Length);
            m.Width = editor.TextWidth(Style.LineNumber, new string('9', digits + 1));
        }

        private void OnContentChanged(int linesAdded)
        {
            if (linesAdded != 0 && cfg.LineNumbers) UpdateLineNumberMargin();
            if (cfg.Preview) { previewTimer.Stop(); previewTimer.Start(); }
        }

        // ───────────────────────────── preview ────────────────────────────

        private void RenderPreview()
        {
            if (!cfg.Preview) return;
            previewReady = false;
            try { browser.DocumentText = MarkdownPreview.ToHtml(editor.Text, TextColor, BackgroundColor); } catch { }
        }

        private void SyncPreviewScroll()
        {
            if (!cfg.Preview || !previewReady) return;
            try
            {
                var doc = browser.Document;
                if (doc?.Body == null) return;
                int maxFirst = Math.Max(1, editor.Lines.Count - editor.LinesOnScreen);
                double ratio = Math.Min(1.0, editor.FirstVisibleLine / (double)maxFirst);
                int height = doc.Body.ScrollRectangle.Height;
                doc.Window.ScrollTo(0, (int)(ratio * Math.Max(0, height - browser.ClientSize.Height)));
            }
            catch { }
        }

        // ───────────────────────────── editing ────────────────────────────

        private Scintilla ActiveText => cheatView.Focused ? cheatView : editor;

        private void Undo() => editor.Undo();
        private void Redo() => editor.Redo();
        private void Cut() => editor.Cut();
        private void Paste() => editor.Paste();

        private void Copy()
        {
            if (browser.ContainsFocus) { try { browser.Document?.ExecCommand("Copy", false, null); } catch { } return; }
            ActiveText.Copy();
        }

        private void SelectAll()
        {
            if (browser.ContainsFocus) { try { browser.Document?.ExecCommand("SelectAll", false, null); } catch { } return; }
            ActiveText.SelectAll();
        }

        public bool PreFilterMessage(ref Message m)
        {
            const int WM_SYSKEYUP = 0x105, WM_SYSKEYDOWN = 0x104, VK_MENU = 0x12;
            if (m.Msg == WM_SYSKEYUP && (int)m.WParam == VK_MENU && altMouseDown)
            {
                altMouseDown = false;
                return true; // swallow: releasing Alt after a column selection must not jump into the menu
            }
            if (m.Msg == WM_SYSKEYDOWN && (int)m.WParam != VK_MENU) altMouseDown = false;
            return false;
        }

        // ───────────────────────────── search ─────────────────────────────

        private IWin32Window FindOwner => findForm != null && !findForm.IsDisposed && findForm.Visible ? findForm : this;

        public void ShowFind(bool replace)
        {
            string sel = editor.SelectedText;
            if (sel.Length > 0 && sel.IndexOfAny(new[] { '\r', '\n' }) < 0) FindText = sel;
            if (findForm != null && !findForm.IsDisposed)
            {
                if (findForm.IsReplace == replace) { findForm.FocusWhat(FindText); return; }
                findForm.Close();
            }
            findForm = new FindForm(this, replace);
            findForm.Show(this);
        }

        public bool FindNext(bool up)
        {
            if (FindText.Length == 0) { ShowFind(false); return false; }
            string t = editor.Text;
            var cmp = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int idx;
            if (!up)
            {
                int start = editor.SelectionEnd;
                idx = start <= t.Length ? t.IndexOf(FindText, start, cmp) : -1;
                if (idx < 0 && WrapAround) idx = t.IndexOf(FindText, 0, cmp);
            }
            else
            {
                int start = editor.SelectionStart - 1;
                idx = start >= 0 ? t.LastIndexOf(FindText, start, cmp) : -1;
                if (idx < 0 && WrapAround && t.Length > 0) idx = t.LastIndexOf(FindText, t.Length - 1, cmp);
            }
            if (idx < 0)
            {
                DarkMsg.Info(FindOwner, $"Не удается найти \"{FindText}\"");
                return false;
            }
            editor.SetSelection(idx + FindText.Length, idx);
            editor.ScrollCaret();
            return true;
        }

        public void ReplaceNext()
        {
            var cmp = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (FindText.Length > 0 && string.Equals(editor.SelectedText, FindText, cmp))
                editor.ReplaceSelection(ReplaceText);
            FindNext(false);
        }

        public void ReplaceAll()
        {
            if (FindText.Length == 0) return;
            int count = 0;
            editor.SearchFlags = MatchCase ? SearchFlags.MatchCase : SearchFlags.None;
            editor.TargetStart = 0;
            editor.TargetEnd = editor.TextLength;
            editor.BeginUndoAction();
            while (editor.SearchInTarget(FindText) != -1)
            {
                editor.ReplaceTarget(ReplaceText);
                count++;
                editor.TargetStart = editor.TargetEnd;
                editor.TargetEnd = editor.TextLength;
            }
            editor.EndUndoAction();
            if (count == 0) DarkMsg.Info(FindOwner, $"Не удается найти \"{FindText}\"");
        }

        private void GoToLine()
        {
            int current = editor.LineFromPosition(editor.CurrentPosition) + 1;
            using var dlg = new GoToForm(current);
            while (dlg.ShowDialog(this) == DialogResult.OK)
            {
                int n = dlg.LineNumber;
                if (n >= 1 && n <= editor.Lines.Count)
                {
                    editor.SetEmptySelection(editor.Lines[n - 1].Position);
                    editor.ScrollCaret();
                    return;
                }
                DarkMsg.Info(this, "Номер строки превышает общее число строк", "BlackNote - Переход на строку");
            }
        }

        // ────────────────────────────── files ─────────────────────────────

        protected override void OnLoad(EventArgs e)
        {
            StartupTrace.Mark("load");
            base.OnLoad(e);
            // Scintilla creates its window as soon as a property is set, so HandleCreated may already be gone.
            Theme.DarkScrollBars(editor.Handle);
            Theme.DarkScrollBars(cheatView.Handle);
            if (startupPath != null) OpenFromCommandLine(startupPath);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        private void OpenFromCommandLine(string path)
        {
            try { path = Path.GetFullPath(path.Trim('"')); } catch { return; }
            if (File.Exists(path)) { LoadFile(path); return; }
            var r = DarkMsg.Show(this, $"Не удается найти файл \"{path}\".\n\nСоздать новый файл?", AppName,
                ("Да", DialogResult.Yes), ("Нет", DialogResult.No));
            if (r != DialogResult.Yes) return;
            try { File.WriteAllBytes(path, Array.Empty<byte>()); LoadFile(path); }
            catch (Exception ex) { DarkMsg.Info(this, ex.Message); }
        }

        private bool ConfirmDiscard()
        {
            if (!IsDirty) return true;
            var r = DarkMsg.Show(this, $"Вы хотите сохранить изменения в файле \"{filePath ?? "Безымянный"}\"?", AppName,
                ("Сохранить", DialogResult.Yes), ("Не сохранять", DialogResult.No), ("Отмена", DialogResult.Cancel));
            if (r == DialogResult.Yes) return Save();
            return r == DialogResult.No;
        }

        private void NewDocument()
        {
            if (!ConfirmDiscard()) return;
            filePath = null;
            encoding = new UTF8Encoding(false);
            encodingName = "UTF-8";
            formatDirty = false;
            editor.EolMode = Eol.CrLf;
            editor.Text = "";
            editor.EmptyUndoBuffer();
            editor.SetSavePoint();
            UpdateTitle();
            UpdateStatusLabels();
            UpdateLineNumberMargin();
            if (cfg.Preview) RenderPreview();
        }

        private static void NewWindow()
        {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false }); } catch { }
        }

        private void OpenDocument()
        {
            if (!ConfirmDiscard()) return;
            using var dlg = new OpenFileDialog { Filter = FileFilter, Title = "Открыть" };
            if (filePath != null) dlg.InitialDirectory = Path.GetDirectoryName(filePath);
            if (dlg.ShowDialog(this) == DialogResult.OK) LoadFile(dlg.FileName);
        }

        private bool LoadFile(string path)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                DarkMsg.Info(this, $"Не удалось открыть файл \"{path}\".\n\n{ex.Message}");
                return false;
            }
            string text = TextCodec.Decode(bytes, out var enc, out string encName);
            Eol eol = TextCodec.DetectEol(text);
            editor.EolMode = eol;
            editor.Text = TextCodec.NormalizeEol(text, eol);
            editor.EmptyUndoBuffer();
            editor.SetSavePoint();
            editor.SetEmptySelection(0);
            editor.ScrollCaret();

            filePath = path;
            encoding = enc;
            encodingName = encName;
            formatDirty = false;
            UpdateTitle();
            UpdateStatusLabels();
            UpdateLineNumberMargin();
            if (cfg.Preview) RenderPreview();
            return true;
        }

        private bool Save() => filePath == null ? SaveAs() : WriteFile(filePath);

        private bool SaveAs()
        {
            using var dlg = new SaveFileDialog
            {
                Filter = FileFilter,
                Title = "Сохранить как",
                AddExtension = true,
                DefaultExt = "txt",
                OverwritePrompt = true,
                FileName = filePath == null ? "*.txt" : Path.GetFileName(filePath),
            };
            if (filePath != null)
            {
                dlg.InitialDirectory = Path.GetDirectoryName(filePath);
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                dlg.FilterIndex = ext == ".txt" ? 1 : ext == ".md" || ext == ".markdown" ? 2 : 3;
            }
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            return WriteFile(dlg.FileName);
        }

        private bool WriteFile(string path)
        {
            string text = editor.Text;
            if (encodingName == "ANSI" && !TextCodec.CanEncode(encoding, text))
            {
                var r = DarkMsg.Show(this,
                    "В тексте есть символы, которых нет в кодировке ANSI, - при сохранении они пропадут.\n\nСохранить файл в UTF-8?",
                    AppName, ("Сохранить в UTF-8", DialogResult.Yes), ("Оставить ANSI", DialogResult.No), ("Отмена", DialogResult.Cancel));
                if (r == DialogResult.Cancel) return false;
                if (r == DialogResult.Yes) { encoding = new UTF8Encoding(false); encodingName = "UTF-8"; }
            }
            try
            {
                byte[] preamble = encoding.GetPreamble();
                byte[] body = encoding.GetBytes(text);
                using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
                fs.Write(preamble, 0, preamble.Length);
                fs.Write(body, 0, body.Length);
            }
            catch (Exception ex)
            {
                DarkMsg.Info(this, $"Не удалось сохранить файл \"{path}\".\n\n{ex.Message}");
                return false;
            }
            filePath = path;
            formatDirty = false;
            editor.SetSavePoint();
            UpdateTitle();
            UpdateStatusLabels();
            return true;
        }

        private void PageSetup()
        {
            try
            {
                pageSettings ??= new PageSettings();
                using var dlg = new PageSetupDialog { PageSettings = pageSettings, EnableMetric = true };
                dlg.ShowDialog(this);
            }
            catch (Exception ex) { DarkMsg.Info(this, "Не удалось открыть параметры страницы.\n\n" + ex.Message); }
        }

        private void Print()
        {
            try
            {
                using var doc = new PrintDocument { DocumentName = DocName };
                if (pageSettings != null) doc.DefaultPageSettings = pageSettings;
                using var dlg = new PrintDialog { Document = doc, UseEXDialog = true };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var style = (cfg.FontBold ? FontStyle.Bold : 0) | (cfg.FontItalic ? FontStyle.Italic : 0);
                using var font = new Font(cfg.FontName, cfg.FontSize, style);
                string remaining = "";
                doc.BeginPrint += (s, e) => remaining = editor.Text.Replace("\t", "    ");
                doc.PrintPage += (s, e) =>
                {
                    e.Graphics.MeasureString(remaining, font, e.MarginBounds.Size, StringFormat.GenericTypographic, out int chars, out _);
                    if (chars <= 0 && remaining.Length > 0) chars = remaining.Length;
                    e.Graphics.DrawString(remaining.Substring(0, chars), font, Brushes.Black, e.MarginBounds, StringFormat.GenericTypographic);
                    remaining = remaining.Substring(chars);
                    e.HasMorePages = remaining.Length > 0;
                };
                doc.Print();
            }
            catch (Exception ex) { DarkMsg.Info(this, "Не удалось напечатать.\n\n" + ex.Message); }
        }

        private void About() => DarkMsg.Info(this,
            "BlackNote 1.00\n\n" +
            "Тёмный текстовый редактор в духе Блокнота Windows - с вертикальным выделением (Alt + мышь) " +
            "и разметкой Markdown как на GitHub.\n\n" +
            "Редактор: Scintilla (scintilla.org), Markdown: Markdig.\n\n" +
            "Автор: AKFRK", "О программе BlackNote");

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
            cfg.Maximized = WindowState == FormWindowState.Maximized;
            cfg.Bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            cfg.CheatWidth = cheatPanel.Width;
            cfg.PreviewWidth = previewPanel.Width;
            cfg.Save();
            if (findForm != null && !findForm.IsDisposed) findForm.Close();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Application.RemoveMessageFilter(this);
            base.OnFormClosed(e);
        }

        // used by SelfTest
        internal Scintilla Editor => editor;
        internal void TestOpenPanels() { SetCheatSheet(true); SetPreview(true); }
        internal void TestShowFind() => ShowFind(true);
        internal FindForm TestFindForm => findForm;
        internal string TestDiagnostics =>
            $"client={ClientSize} status={status.Bounds} auto={status.AutoSize} pos={lblPos.Bounds} editor={editor.Bounds} font={status.Font.Height}";
    }
}
