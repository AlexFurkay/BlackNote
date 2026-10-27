using System;
using System.Drawing;
using System.Windows.Forms;

namespace BlackNote
{
    /// <summary>Base for small dark dialogs. Coordinates are given in 96-DPI pixels.</summary>
    internal class DarkDialog : Form
    {
        public DarkDialog()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Menu;
            ForeColor = Theme.Text;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.None;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        protected static int S(int v) => Theme.S(v);

        public static Button MakeButton(string text)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Button,
                ForeColor = Theme.Text,
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = Theme.Border;
            b.FlatAppearance.MouseOverBackColor = Theme.Hover;
            b.FlatAppearance.MouseDownBackColor = Theme.Border;
            return b;
        }

        protected Button AddButton(string text, int x, int y, int w = 96, int h = 28)
        {
            var b = MakeButton(text);
            b.SetBounds(S(x), S(y), S(w), S(h));
            Controls.Add(b);
            return b;
        }

        protected Label AddLabel(string text, int x, int y)
        {
            var l = new Label { Text = text, AutoSize = true, Location = new Point(S(x), S(y)), ForeColor = Theme.Text };
            Controls.Add(l);
            return l;
        }

        protected TextBox AddInput(int x, int y, int w)
        {
            var t = new TextBox
            {
                Location = new Point(S(x), S(y)),
                Width = S(w),
                BackColor = Theme.Input,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
            };
            Controls.Add(t);
            return t;
        }

        protected CheckBox AddCheck(string text, int x, int y)
        {
            var c = new CheckBox { Text = text, AutoSize = true, Location = new Point(S(x), S(y)), ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat };
            c.FlatAppearance.BorderColor = Theme.Text;
            c.FlatAppearance.CheckedBackColor = Theme.Menu;
            Controls.Add(c);
            return c;
        }

        protected RadioButton AddRadio(string text, int x, int y)
        {
            var r = new RadioButton { Text = text, AutoSize = true, Location = new Point(S(x), S(y)), ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat };
            r.FlatAppearance.BorderColor = Theme.Text;
            r.FlatAppearance.CheckedBackColor = Theme.Menu;
            Controls.Add(r);
            return r;
        }
    }

    /// <summary>Dark replacement for MessageBox.</summary>
    internal sealed class DarkMsg : DarkDialog
    {
        private DarkMsg(string text, string title, (string Text, DialogResult Result)[] buttons)
        {
            Text = title;
            int maxWidth = S(440);
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(maxWidth, 0),
                Location = new Point(S(20), S(22)),
                ForeColor = Theme.Text,
            };
            Controls.Add(label);
            Size textSize = label.GetPreferredSize(new Size(maxWidth, 0));

            int bw = S(118), bh = S(28), gap = S(8);
            int rowWidth = buttons.Length * bw + (buttons.Length - 1) * gap;
            int width = Math.Max(Math.Max(S(340), textSize.Width + S(40)), rowWidth + S(40));

            var footer = new Panel
            {
                Location = new Point(0, label.Top + textSize.Height + S(22)),
                Size = new Size(width, bh + S(26)),
                BackColor = Theme.Window,
            };
            Controls.Add(footer);

            int x = width - S(20) - rowWidth;
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = MakeButton(buttons[i].Text);
                b.SetBounds(x + i * (bw + gap), S(13), bw, bh);
                b.DialogResult = buttons[i].Result;
                footer.Controls.Add(b);
                if (i == 0) AcceptButton = b;
                if (buttons[i].Result == DialogResult.Cancel || buttons.Length == 1) CancelButton = b;
            }
            ClientSize = new Size(width, footer.Bottom);
        }

        public static DialogResult Show(IWin32Window owner, string text, string title, params (string, DialogResult)[] buttons)
        {
            using var f = new DarkMsg(text, title, buttons);
            if (owner == null) f.StartPosition = FormStartPosition.CenterScreen;
            return f.ShowDialog(owner);
        }

        public static void Info(IWin32Window owner, string text, string title = "BlackNote") =>
            Show(owner, text, title, ("ОК", DialogResult.OK));
    }

    /// <summary>Modeless "Find" / "Replace" window, like in Notepad.</summary>
    internal sealed class FindForm : DarkDialog
    {
        private readonly MainForm main;
        private readonly TextBox what, with;
        private readonly CheckBox matchCase, wrap;
        private readonly RadioButton up, down;
        private readonly Button findNext, replaceOne, replaceAll;

        public bool IsReplace { get; }

        public FindForm(MainForm main, bool replace)
        {
            this.main = main;
            IsReplace = replace;
            Text = replace ? "Заменить" : "Найти";
            StartPosition = FormStartPosition.Manual;

            const int inputX = 92, inputW = 236, btnX = 344, btnW = 116;
            AddLabel("Что:", 12, 15);
            what = AddInput(inputX, 12, inputW);
            findNext = AddButton("Найти далее", btnX, 10, btnW);
            Button cancel;

            if (replace)
            {
                AddLabel("Чем:", 12, 47);
                with = AddInput(inputX, 44, inputW);
                replaceOne = AddButton("Заменить", btnX, 42, btnW);
                replaceAll = AddButton("Заменить все", btnX, 74, btnW);
                cancel = AddButton("Отмена", btnX, 106, btnW);
                matchCase = AddCheck("С учетом регистра", 12, 88);
                wrap = AddCheck("Обтекание текстом", 12, 114);
                ClientSize = new Size(S(472), S(146));
            }
            else
            {
                cancel = AddButton("Отмена", btnX, 42, btnW);
                AddLabel("Направление", 196, 50);
                up = AddRadio("Вверх", 196, 72);
                down = AddRadio("Вниз", 268, 72);
                matchCase = AddCheck("С учетом регистра", 12, 62);
                wrap = AddCheck("Обтекание текстом", 12, 88);
                ClientSize = new Size(S(472), S(118));
            }

            what.Text = main.FindText;
            if (with != null) with.Text = main.ReplaceText;
            matchCase.Checked = main.MatchCase;
            wrap.Checked = main.WrapAround;
            if (up != null) { up.Checked = main.SearchUp; down.Checked = !main.SearchUp; }

            what.TextChanged += (s, e) => UpdateButtons();
            findNext.Click += (s, e) => { Sync(); main.FindNext(up != null && up.Checked); };
            if (replace)
            {
                replaceOne.Click += (s, e) => { Sync(); main.ReplaceNext(); };
                replaceAll.Click += (s, e) => { Sync(); main.ReplaceAll(); };
            }
            cancel.Click += (s, e) => Close();
            AcceptButton = findNext;
            CancelButton = cancel;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool has = what.Text.Length > 0;
            findNext.Enabled = has;
            if (replaceOne != null) { replaceOne.Enabled = has; replaceAll.Enabled = has; }
        }

        private void Sync()
        {
            main.FindText = what.Text;
            if (with != null) main.ReplaceText = with.Text;
            main.MatchCase = matchCase.Checked;
            main.WrapAround = wrap.Checked;
            if (up != null) main.SearchUp = up.Checked;
        }

        public void FocusWhat(string text)
        {
            if (!string.IsNullOrEmpty(text)) what.Text = text;
            Activate();
            what.Focus();
            what.SelectAll();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (Owner != null) Location = new Point(Owner.Left + (Owner.Width - Width) / 2, Owner.Top + S(110));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            what.Focus();
            what.SelectAll();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Sync();
            base.OnFormClosing(e);
        }
    }

    /// <summary>"Go to line" dialog.</summary>
    internal sealed class GoToForm : DarkDialog
    {
        private readonly TextBox input;

        public int LineNumber { get; private set; }

        public GoToForm(int current)
        {
            Text = "Переход на строку";
            AddLabel("Номер строки:", 12, 12);
            input = AddInput(12, 34, 300);
            input.Text = current.ToString();
            input.KeyPress += (s, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            var ok = AddButton("Перейти", 112, 72);
            var cancel = AddButton("Отмена", 216, 72);
            ok.Click += (s, e) =>
            {
                if (int.TryParse(input.Text, out int n)) { LineNumber = n; DialogResult = DialogResult.OK; }
            };
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(S(324), S(112));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            input.Focus();
            input.SelectAll();
        }
    }
}
