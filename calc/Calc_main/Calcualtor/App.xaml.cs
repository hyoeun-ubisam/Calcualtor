using Calculator.Utils;
using log4net;
using log4net.Core;
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
            // log4net은 App.config의 FileAppender가 관리!
            var safeFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CalculatorApp", "logs");

            // JSON 감사 로그 파일 (고정형)
            var jsonPath = Path.Combine(safeFolder, "Client_json.log");

            // 디렉터리는 Log.InitFile 내부에서도 보장하지만, 여기도 만들어 두면 더 안전함
            Directory.CreateDirectory(safeFolder);

            var ok = Log.InitFile(jsonPath, append: true);

            MessageBox.Show(
                ok ? $"JSON log initialized:\n{jsonPath}" : "JSON log init failed",
                "Client Log", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);

            System.Diagnostics.Debug.WriteLine($"[App] Log.InitFile({jsonPath}) => {ok}");
            #endregion
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 파일 깔끔히 닫기
            Log.Close();
            base.OnExit(e);
        }
    }
}
