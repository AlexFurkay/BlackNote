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
            StartupTrace.Mark("Main");
            // Scintilla.NET checks the Authenticode signatures of the bundled native DLLs on start.
            // That check goes online for certificate revocation and stalls ~10 s per DLL when the
            // CA server is unreachable (it is blocked for some ISPs). The DLLs ship with the app.
            ScintillaNET.ScintillaNativeLibrary.VerifyScintillaDll = false;
            ScintillaNET.ScintillaNativeLibrary.VerifyLexillaDll = false;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // ANSI code pages (1251 etc.)
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Init();
            StartupTrace.Mark("theme");
            MarkdownPreview.EnableModernBrowser();
            StartupTrace.Mark("browser registry");

            if (args.Length >= 2 && args[0] == "--selftest")
            {
                SelfTest.Run(args[1]);
                return;
            }
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }
    }
}
