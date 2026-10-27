using System;
using System.Text;
using System.Windows.Forms;

namespace BlackNote
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // ANSI code pages (1251 etc.)
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Init();
            MarkdownPreview.EnableModernBrowser();

            if (args.Length >= 2 && args[0] == "--selftest")
            {
                SelfTest.Run(args[1]);
                return;
            }
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }
    }
}
