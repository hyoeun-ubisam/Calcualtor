using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Calculator.Models;

namespace CalculatorApp.ViewModel
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        public static event EventHandler<SettingsSavedEventArgs>? SettingsSaved;

        private string _serverAddress = "https://localhost";
        public string ServerAddress     //  서버 주소
        {
            get => _serverAddress;
            set { _serverAddress = value; OnPropertyChanged(); }
        }

        private string _serverPort = "5001";
        public string ServerPort        //  서버 포트   
        {
            get => _serverPort;
            set { _serverPort = value; OnPropertyChanged(); }
        }

        private string _token = string.Empty;
        public string Token          //  API 토큰
        {
            get => _token;
            set { _token = value; OnPropertyChanged(); }
        }

        public ICommand SaveCommand { get; }
        public ICommand TestConnectionCommand { get; }

        private const string AppFolderName = "CalculatorApp";

        public SettingsViewModel()         
        {
            var data = ReadSettingsFromFile();          //  파일에서 설정 읽기
            if (data != null)
            {
                ServerAddress = data.ServerAddress ?? ServerAddress;
                ServerPort = data.ServerPort.ToString();
                Token = data.Token ?? "";
            }

            SaveCommand = new RelayCommand(async (p) => await SaveAsync(p));
            TestConnectionCommand = new RelayCommand(async (p) => await TestConnectionAsync());
        }

        #region Save / Test

        private async Task SaveAsync(object? parameter)         //  설정 저장
        {
            try
            {
                if (!int.TryParse(ServerPort, out var port) || port < 1 || port > 65535)
                {
                    MessageBox.Show("포트는 1 ~ 65535 사이의 정수여야 합니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(ServerAddress))
                {
                    MessageBox.Show("서버 주소를 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var raw = ServerAddress.Trim();
                raw = raw.TrimEnd('/', ':');

                Uri baseUri;
                if (Uri.TryCreate(raw, UriKind.Absolute, out var parsed) &&
                    (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
                {
                    baseUri = new UriBuilder(parsed.Scheme, parsed.Host, port, "calc").Uri;
                }
                else
                {
                    baseUri = new UriBuilder(Uri.UriSchemeHttps, raw, port, "calc").Uri;
                }

                var finalBase = baseUri.AbsoluteUri;
                if (!finalBase.EndsWith("/")) finalBase += "/";

                var path = GetSettingsPath();

                string serverAddrForJson;
                int serverPortForJson;
                if (Uri.TryCreate(finalBase, UriKind.Absolute, out var baseUri2))
                {
                    serverAddrForJson = $"{baseUri2.Scheme}://{baseUri2.Host}";
                    serverPortForJson = baseUri2.Port;
                }
                else
                {
                    serverAddrForJson = ServerAddress;
                    serverPortForJson = port;
                }

                var apiSettings = new JObject
                {
                    ["BaseUrl"] = finalBase,
                    ["ServerAddress"] = serverAddrForJson,
                    ["ServerPort"] = serverPortForJson,
                    ["Token"] = Token ?? ""
                };

                var root = new JObject(new JProperty("ApiSettings", apiSettings));          // JSON 형식으로 저장

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);           
                File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented));

                SettingsSaved?.Invoke(this, new SettingsSavedEventArgs { BaseUrl = finalBase, Token = Token });

                MessageBox.Show("설정이 저장되었습니다.", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);

                if (parameter is Window w) w.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 저장 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task TestConnectionAsync()            //  서버 연결 테스트
        {
            var baseUrl = BuildBaseUrlForTest(ServerAddress, ServerPort);
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(3) };
                if (!string.IsNullOrWhiteSpace(Token))
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

                using var resp = await http.GetAsync("health");
                if (resp.IsSuccessStatusCode)
                {
                    MessageBox.Show("서버 연결 성공", "연결 테스트", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"서버 응답 : {(int)resp.StatusCode}", "연결 테스트", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"서버에 연결할 수 없습니다:\n{ex.Message}", "연결 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region File I/O

        private static string GetSettingsPath()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "user_settings.json");
        }

        private SettingsData? ReadSettingsFromFile()
        {
            try
            {
                var path = GetSettingsPath();
                if (!File.Exists(path)) return null;

                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return null;

                var j = JObject.Parse(text);
                var api = j["ApiSettings"];
                if (api == null) return null;

                // 우선적으로 ServerAddress / ServerPort 를 읽자
                var addr = api["ServerAddress"]?.Value<string>();
                var port = api["ServerPort"]?.Value<int?>();
                var token = api["Token"]?.Value<string>();

                // explicit fields가 없으면 BaseUrl을 파싱해서 얻는다
                if (string.IsNullOrWhiteSpace(addr) || !port.HasValue)
                {
                    var baseUrl = api["BaseUrl"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var u))
                    {
                        addr = $"{u.Scheme}://{u.Host}";
                        port = u.Port;
                    }
                }

                var data = new SettingsData();
                if (!string.IsNullOrWhiteSpace(addr)) data.ServerAddress = addr;
                if (port.HasValue) data.ServerPort = port.Value;
                if (!string.IsNullOrWhiteSpace(token)) data.Token = token;

                return data;
            }
            catch
            {
                // 읽기 실패시 null 반환 (caller에서 기본값 사용)
                return null;
            }
        }

        #endregion

        #region Helpers

        private static string BuildBaseUrlForTest(string addr, string portText)
        {
            if (!int.TryParse(portText, out var port)) port = 5001;
            if (Uri.TryCreate(addr, UriKind.Absolute, out var u) &&
                (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            {
                var builder = new UriBuilder(u.Scheme, u.Host, port);
                return EnsureTrailingSlash(builder.Uri.AbsoluteUri);
            }
            else
            {
                var builder = new UriBuilder("https", addr, port);
                return EnsureTrailingSlash(builder.Uri.AbsoluteUri);
            }
        }

        private static string EnsureTrailingSlash(string url) => url.EndsWith("/") ? url : url + "/";

        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        #endregion
    }

}
