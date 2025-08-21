using Calculator.Utils;
using log4net.Util;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Calculator
{
    public partial class App : Application
    {
        public static IConfiguration Configuration { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            Log.Enabled = true;
            #region
            var safeFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "logs");

            var jsonPath = Path.Combine(safeFolder, "Client_log.json");

            Directory.CreateDirectory(safeFolder);

            var ok = Log.InitFile(jsonPath, append: true);

            MessageBox.Show(
                ok ? $"Log file initialized:\n{jsonPath}" : "Log init failed",
                "Client Log", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);

            System.Diagnostics.Debug.WriteLine($"[App] Log.InitFile({jsonPath}) => {ok}");
#endregion
            base.OnStartup(e);
            
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Close();
            base.OnExit(e);
        }
    }
}
