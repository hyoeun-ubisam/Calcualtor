using Calculator.Utils;
using Calculator.Models;
using CalculatorApp.Model;
using log4net;
using log4net.Config;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CalculatorApp.ViewModel
{
    public class CalculatorViewModel : INotifyPropertyChanged, IDisposable
    {
        private bool _isRetryingConnection = false;
        private CancellationTokenSource _retryCts = new CancellationTokenSource();          

        private bool _isServerOnline;
        public bool IsServerOnline          // 서버 연결 상태 확인
        {
            get => _isServerOnline;
            set
            {
                if (_isServerOnline != value)
                {
                    _isServerOnline = value;
                    OnPropertyChanged();
                    Application.Current?.Dispatcher?.Invoke(CommandManager.InvalidateRequerySuggested);         // ExcuteCommand 상태 업데이트
                }
            }
        }

        private static readonly ILog log = LogManager.GetLogger(typeof(CalculatorViewModel));

        private HttpClient _httpClient;

        private string _apiBaseUrl = "https://localhost:5001/calc/";           
        private string _token = "secret_token_123";

        private string? _recordText;
        public string? RecordText           // 계산 기록
        {
            get => _recordText;
            set { if (_recordText != value) { _recordText = value; OnPropertyChanged(); } }
        }

        private string? _resultText;        // 계산 결과
        public string? ResultText
        {
            get => _resultText;
            set { if (_resultText != value) { _resultText = value; OnPropertyChanged(); } }
        }

        private string? _accumulator;
        private string? _pendingOp;
        private string? _pendingOpDisplay;
        private string? _lastOperand;
        private string? _lastOperator;
        private string? _lastOperatorDisplay;
        private string? _lastLeftOperand;
        private bool _awaitingSecondOperand;
        private bool _justEvaluated;

        private static readonly string[] Operators = { "+", "-", "×", "÷" };

        public ICommand ButtonClickCommand { get; private set; }        // 바인딩을 통한 버튼 클릭 명령

        public CalculatorViewModel()        
        {
            XmlConfigurator.Configure();
#if DEBUG
            var handler = new HttpClientHandler         // 서버 인증 (하지만 이 코드에선 SSL 인증 무시)
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler);
#else
            _httpClient = new HttpClient();
#endif
            _httpClient.Timeout = TimeSpan.FromSeconds(5);

            ButtonClickCommand = new RelayCommand(ExecuteButtonClick, (param) => IsServerOnline);       // 서버 연결 상태에 따른 버튼 활성화
            ResultText = "0";
            RecordText = "";
            ResetAll();

            TryApplyBaseAddressToHttpClient();

            SettingsViewModel.SettingsSaved += OnSettingsSaved;

            Task.Run(() => InitializeViewModelAsync());
        }

        private void OnSettingsSaved(object? sender, SettingsSavedEventArgs e)          // 서버와 연결 설정이 변경되었을 때 호출
        {
            UpdateSettings(e.BaseUrl, string.IsNullOrWhiteSpace(e.Token) ? null : e.Token);
        }

        /// <summary>
        /// 반영: newBaseUrl은 절대(끝에 슬래시 포함) 또는 상대적으로 들어오면 정규화하여 사용.
        /// newToken이 null이면 기존 _token 유지; 빈 문자열이면 토큰 제거.
        /// </summary>
        public void UpdateSettings(string newBaseUrl, string? newToken)     // 서버 주소와 토큰을 업데이트
        {
            if (string.IsNullOrWhiteSpace(newBaseUrl)) return;

            if (!newBaseUrl.EndsWith("/")) newBaseUrl += "/";

            _apiBaseUrl = newBaseUrl;

            if (newToken is null) { }
            else { _token = newToken; }

            try { _httpClient?.Dispose(); }
            catch { }

#if DEBUG
            var handler = new HttpClientHandler      
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler);
#else
            _httpClient = new HttpClient();
#endif
            _httpClient.Timeout = TimeSpan.FromSeconds(5);

            try
            {
                _httpClient.BaseAddress = new Uri(_apiBaseUrl);
            }
            catch (Exception ex)
            {
                log.Warn("Apply base address failed, falling back to default", ex);
                _httpClient.BaseAddress = new Uri("https://localhost:5001/calc/");
            }

            if (!string.IsNullOrWhiteSpace(_token))
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            else
                _httpClient.DefaultRequestHeaders.Authorization = null;

#if DEBUG
            Debug.WriteLine($"[TryApply] Applied BaseAddress={_httpClient.BaseAddress}, TokenPresent={!string.IsNullOrWhiteSpace(_token)}");
#endif

            try { _retryCts.Cancel(); } catch { }
            try { _retryCts.Dispose(); } catch { }

            _retryCts = new CancellationTokenSource();          // 재시도 토큰 초기화

            // 서버 연결 상태 초기화
            _ = Task.Run(async () =>        
            {
                var ok = await CheckServerConnectionAsync();
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (ok)
                    {
                        IsServerOnline = true;
                        ResultText = "0";
                    }
                    else
                    {
                        IsServerOnline = false;
                        ResultText = "연결 오류";
                        StartConnectionRetryLoop();
                    }
                });
            });
        }

        private void TryApplyBaseAddressToHttpClient()      // 초기화 시점에 기본 주소 설정
        {
            try
            {
                _apiBaseUrl = EnsureTrailingSlash(_apiBaseUrl);
                if (_httpClient != null && !string.IsNullOrWhiteSpace(_apiBaseUrl))
                    _httpClient.BaseAddress = new Uri(_apiBaseUrl);
            }
            catch (Exception ex)
            {
                log.Warn("BaseAddress 설정 중 오류", ex);
            }

            if (!string.IsNullOrWhiteSpace(_token))
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            else
                _httpClient.DefaultRequestHeaders.Authorization = null;

#if DEBUG
            Debug.WriteLine($"[TryApply] BaseAddress={_httpClient.BaseAddress}, TokenPresent={!string.IsNullOrWhiteSpace(_token)}");
#endif
        }

        private static string EnsureTrailingSlash(string url) => string.IsNullOrEmpty(url) ? url : (url.EndsWith("/") ? url : url + "/");       // URL 형식 맞추기

        private async Task InitializeViewModelAsync()           // 첫 실행 시, 서버 연결 확인
        {
            IsServerOnline = false;
            ResultText = "서버 연결 중...";

            bool isConnected = await CheckServerConnectionAsync();
            if (isConnected)
            {
                IsServerOnline = true;
                ResultText = "0";
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("서버가 연결되어 있지 않습니다. 연결 후 다시 시도해 주세요.", "연결 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    ResultText = "연결 오류";
                });
                StartConnectionRetryLoop();
            }
        }

        private void StartConnectionRetryLoop()         // 서버 연결 재시도
        {
            if (_isRetryingConnection) return;
            _isRetryingConnection = true;           

            Task.Run(async () =>
            {
                log.Info("서버 연결 재시도");
                while (!_isServerOnline && !_retryCts.IsCancellationRequested)          //서버 연결이 되지 않았고, 취소되지 않은 경우
                {
                    await Task.Delay(3000, _retryCts.Token);            // 3초 간격으로 재시도

                    if (await CheckServerConnectionAsync())
                    {
                        log.Info("서버 연결에 성공");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            IsServerOnline = true;
                            ResultText = "0";
                        });

                        _isRetryingConnection = false;
                        break;
                    }
                    else
                    {
                        log.Debug("서버 연결 재시도 실패. 3초 후 다시 시도합니다.");
                    }
                }
                _isRetryingConnection = false;
                log.Info("서버 연결 재시도를 종료합니다.");
            }, _retryCts.Token);
        }

        private async Task<bool> CheckServerConnectionAsync()       // 서버 연결 상태 확인
        {
            string baseAddr = _httpClient?.BaseAddress?.ToString() ?? _apiBaseUrl;
            baseAddr = EnsureTrailingSlash(baseAddr);

#if DEBUG
            Debug.WriteLine($"[HealthCheck] BaseAddress={baseAddr}");

#endif      // baseAddr가 비어있으면 연결 실패
            try
            {
                var healthUri = new Uri(new Uri(baseAddr), "health");
#if DEBUG
                Debug.WriteLine($"[HealthCheck] Trying primary URI: {healthUri}");
#endif
                using var req = new HttpRequestMessage(HttpMethod.Get, healthUri);
                if (!string.IsNullOrWhiteSpace(_token))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

                using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
#if DEBUG
                Debug.WriteLine($"[HealthCheck] Primary status={(int)resp.StatusCode}");
#endif
                if (resp.IsSuccessStatusCode) return true;
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[HealthCheck] Primary failed: {ex.GetType().Name} - {ex.Message}");
#endif
            }

            // baseAddr가 HTTPS가 아니거나, baseAddr가 비어있으면 HTTPS로 시도
            if (Uri.TryCreate(baseAddr, UriKind.Absolute, out var baseUri) && baseUri.Scheme != Uri.UriSchemeHttps)
            {
                var httpsBuilder = new UriBuilder(baseUri) { Scheme = Uri.UriSchemeHttps, Port = baseUri.Port };
                var httpsBase = EnsureTrailingSlash(httpsBuilder.Uri.AbsoluteUri);
#if DEBUG
                Debug.WriteLine($"[HealthCheck] Trying fallback HTTPS base: {httpsBase}");
#endif
#if DEBUG
                var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (m, c, ch, e) => true };
                using var client = new HttpClient(handler) { BaseAddress = new Uri(httpsBase), Timeout = TimeSpan.FromSeconds(5) };
#else
                using var client = new HttpClient() { BaseAddress = new Uri(httpsBase), Timeout = TimeSpan.FromSeconds(5) };
#endif
                try
                {
                    using var req2 = new HttpRequestMessage(HttpMethod.Get, "health");
                    if (!string.IsNullOrWhiteSpace(_token))
                        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

                    using var resp2 = await client.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead);
#if DEBUG
                    Debug.WriteLine($"[HealthCheck] Fallback status={(int)resp2.StatusCode}");
#endif
                    return resp2.IsSuccessStatusCode;
                }
                catch (Exception ex2)
                {
#if DEBUG
                    Debug.WriteLine($"[HealthCheck] Fallback failed: {ex2.GetType().Name} - {ex2.Message}");
#endif
                    return false;
                }
            }

            return false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));          

        private void ResetAll()         // 모든 상태를 초기화
        {
            _accumulator = null;
            _pendingOp = null;
            _pendingOpDisplay = null;
            _lastOperand = null;
            _lastOperator = null;
            _lastOperatorDisplay = null;
            _awaitingSecondOperand = false;
            _justEvaluated = false;
            RecordText = "";
            ResultText = "0";
        }

        private void ResetStateButKeepDisplay(bool keepRecord)          // 상태는 초기화하되, 화면 표시 유지
        {
            _accumulator = null;
            _pendingOp = null;
            _pendingOpDisplay = null;
            _lastOperand = null;
            _lastOperator = null;
            _lastOperatorDisplay = null;
            _awaitingSecondOperand = false;
            _justEvaluated = false;

            if (!keepRecord)
            {
                RecordText = "";
            }
        }

        private void UpdateRecordForTyping()            // 현재 계산 기록 업데이트
        {
            if (_accumulator != null && _pendingOpDisplay != null)
                RecordText = $"{_accumulator} {_pendingOpDisplay} {ResultText}";
            else
                RecordText = ResultText;
        }

        private async void ExecuteButtonClick(object parameter)         // 계산기 기능 처리
        {
            var value = parameter?.ToString();
            if (string.IsNullOrWhiteSpace(value)) return;
            var current = ResultText ?? "0";

            switch (value)
            {
                case "C":
                    ResetAll();
                    return;
                case "←":
                    if (_justEvaluated)
                    {
                        RecordText = "";
                        _pendingOpDisplay = null;
                        return;
                    }
                    if (_awaitingSecondOperand && _pendingOp != null)
                    {
                        _pendingOp = null;
                        _awaitingSecondOperand = false;
                        RecordText = _accumulator ?? "";
                        return;
                    }
                    if (current.Length > 1)
                        ResultText = current[..^1];
                    else
                        ResultText = "0";

                    UpdateRecordForTyping();
                    return;
                case "±":
                    if (double.TryParse(ResultText, NumberStyles.Float, CultureInfo.CurrentCulture, out var n))
                        ResultText = (-n).ToString(CultureInfo.CurrentCulture);
                    UpdateRecordForTyping();
                    _justEvaluated = false;
                    return;
                case "%":
                    ApplyPercent();
                    return;
                case "=":
                    await HandleEqualsAsync();
                    return;
            }

            bool isOp = IsOpToken(value);
            if (isOp)
            {
                await HandleOperatorAsync(value);
                return;
            }

            HandleNumberInput(value);
        }

        private void HandleNumberInput(string value)            // 숫자 입력 처리
        {
            var current = ResultText;
            if (_justEvaluated)
            {
                _justEvaluated = false;
                _accumulator = null;
                _pendingOp = null;
                _pendingOpDisplay = null;
                _lastOperand = null;
                _lastOperator = null;
                _lastOperatorDisplay = null;
                _awaitingSecondOperand = false;
                RecordText = "";
                current = "0";
                ResultText = "0";
            }

            if (_awaitingSecondOperand)
            {
                ResultText = value == "." ? "0." : value;
                _awaitingSecondOperand = false;
            }
            else
            {
                if (value == ".")
                {
                    if (!current.Contains("."))
                        ResultText = current + ".";
                }
                else
                {
                    if (current == "0")
                        ResultText = value;
                    else
                        ResultText = current + value;
                }
            }

            UpdateRecordForTyping();
        }

        private async Task HandleOperatorAsync(string opBtn)            // 연산자 처리
        {
            var current = ResultText;
            if (_accumulator == null || _justEvaluated)
            {
                _accumulator = current;
                _pendingOp = NormalizeOperator(opBtn);
                _pendingOpDisplay = opBtn;
                _awaitingSecondOperand = true;
                _justEvaluated = false;
                RecordText = $"{_accumulator} {_pendingOpDisplay}";
                return;
            }

            if (_pendingOp != null && IsNumber(current) && !_awaitingSecondOperand)
            {
                var r = await ComputeBinaryAsync(_accumulator!, current, _pendingOp!);
                if (r.success)
                {
                    _accumulator = r.result;
                    _pendingOp = NormalizeOperator(opBtn);
                    _pendingOpDisplay = opBtn;
                    _awaitingSecondOperand = true;
                    RecordText = $"{_accumulator} {_pendingOpDisplay}";
                    _lastOperand = null;
                    _lastOperator = null;
                    _lastOperatorDisplay = null;
                    ResultText = _accumulator;
                }
                else
                {
                    ShowError(r.error);
                }
                return;
            }

            _pendingOp = NormalizeOperator(opBtn);
            _pendingOpDisplay = opBtn;
            _awaitingSecondOperand = true;
            RecordText = $"{_accumulator} {_pendingOpDisplay}";
        }

        private async Task HandleEqualsAsync()          // = 연산 처리
        {
            var current = ResultText;
            if (_accumulator != null && _pendingOp != null && IsNumber(current))
            {
                var leftDisp = _accumulator;
                var opDisp = _pendingOpDisplay ?? _pendingOp;
                var rightDisp = current;

                Log.Request(NormalizeOperator(opDisp), leftDisp, rightDisp);

                var r = await ComputeBinaryAsync(_accumulator!, current, _pendingOp!);
                if (r.success)
                {
                    _lastOperand = rightDisp;
                    _lastOperator = _pendingOp;
                    _lastOperatorDisplay = opDisp;
                    _lastLeftOperand = leftDisp;

                    _accumulator = r.result;
                    _pendingOp = null;
                    _pendingOpDisplay = null;

                    RecordText = $"{leftDisp} {opDisp} {rightDisp} =";
                    ResultText = _accumulator;
                    _justEvaluated = true;
                    _awaitingSecondOperand = false;
                }
                else
                {
                    ShowError(r.error);
                }
                return;
            }

            if (_accumulator != null && _lastOperand != null && _lastOperator != null && _pendingOp == null)
            {
                Log.Request(_lastOperator, _accumulator, _lastOperand);         // Log(요청) 기록

                var r = await ComputeBinaryAsync(_accumulator!, _lastOperand!, _lastOperator!);
                if (r.success)
                {
                    RecordText = $"{_accumulator} {_lastOperatorDisplay ?? _lastOperator} {_lastOperand} =";
                    _accumulator = r.result;
                    ResultText = _accumulator;
                    _justEvaluated = true;
                }
                else
                {
                    ShowError(r.error);
                }
            }
        }

        private void ApplyPercent()         // % 연산 처리
        {
            if (!double.TryParse(ResultText, NumberStyles.Float, CultureInfo.CurrentCulture, out var right))
                return;

            if (_accumulator != null && _pendingOp != null &&
                double.TryParse(_accumulator, NumberStyles.Float, CultureInfo.CurrentCulture, out var acc))
            {
                var opDisp = _pendingOpDisplay ?? _pendingOp;
                if (opDisp is "+" or "−" or "-")
                    right = acc * right / 100.0;
                else
                    right = right / 100.0;

                ResultText = right.ToString(CultureInfo.CurrentCulture);
                UpdateRecordForTyping();
                _justEvaluated = false;
                return;
            }

            if (_justEvaluated && _pendingOp == null)
            {
                var x = right;
                var p = x * x / 100.0;
                ResultText = p.ToString(CultureInfo.CurrentCulture);
                RecordText = $"{x} %";
                _justEvaluated = false;
                return;
            }

            if (_accumulator == null && _pendingOp == null)
            {
                ResultText = "0";
                RecordText = "";
                return;
            }

            ResultText = "0";
            RecordText = "";
        }

        private void ShowError(string? error)
        {
            RecordText = "";
            ResultText = error ?? "Error";
            ResetStateButKeepDisplay(true);
        }

        private static string NormalizeOperator(string op) => op switch { "×" => "*", "÷" => "/", "−" => "-", _ => op };
        private static bool IsNumber(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out _);

        public async Task<(bool success, string? result, string? error)>
        ComputeBinaryAsync(string num1, string num2, string opBtn)            // 서버에 계산 요청을 보내고 결과 반환
        {
            var op = NormalizeOperator(opBtn);

            var reqObj = new CalculationRequest
            {
                Op = op,
                Num1 = ToInvariant(num1),
                Num2 = ToInvariant(num2)
            };

            var json = JsonConvert.SerializeObject(reqObj);         // JSON 직렬화

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, "compute")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(_token))
                httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            try
            {
                using var resp = await _httpClient.SendAsync(httpReq);
                var body = await resp.Content.ReadAsStringAsync();
                var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";

                Log.Meta(new Uri(_httpClient.BaseAddress!, "compute").ToString(), (int)resp.StatusCode, contentType);

#if DEBUG
                var baseAddr = _httpClient?.BaseAddress?.ToString() ?? _apiBaseUrl;
                Debug.WriteLine($"POST {baseAddr}compute");
                Debug.WriteLine($"Request Body: {json}");
                Debug.WriteLine($"Status: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                Debug.WriteLine($"Content-Type: {contentType}");
                Debug.WriteLine($"Response Body: {body}");
#endif

                if (resp.IsSuccessStatusCode)           // 성공 응답 처리
                {
                    var resObj = JsonConvert.DeserializeObject<CalculationResponse>(body);
                    var resStr = resObj?.Result;
                    if (resStr != null &&
                        double.TryParse(resStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                    {
                        Log.Response(resStr);           // Log(응답) 기록
                        return (true, num.ToString("G15", CultureInfo.InvariantCulture), null);
                    }
                    Log.Error("Unexpected response payload", (int)resp.StatusCode);
                    return (false, null, "Unexpected response");
                }
                else if (resp.StatusCode == HttpStatusCode.Unauthorized)        // 401 인증 실패
                {
                    Log.Error("Unauthorized", (int)resp.StatusCode);
                    // Authorization missing or incorrect
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("서버 인증에 실패했습니다. 설정에서 토큰을 확인하세요.", "인증 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                        // Optionally open settings window
                    });
                    return (false, null, "인증 실패");
                }
                else if (resp.StatusCode >= HttpStatusCode.InternalServerError)         // 500 이상 서버 오류
                {
                    Log.Error($"Server error {(int)resp.StatusCode}", (int)resp.StatusCode);
                    var errorMsg = "서버에 일시적인 문제가 발생했습니다. 잠시 후 다시 시도해 주세요.";
                    log.Error($"서버 오류 발생 (Code: {(int)resp.StatusCode}). Request: {json}, Response: {body}");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(errorMsg, "서버 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    return (false, null, "서버 오류");
                }
                else
                {
                    var resObj = JsonConvert.DeserializeObject<CalculationResponse>(body);
                    var errMsg = !string.IsNullOrWhiteSpace(resObj?.Error) ? resObj!.Error : $"HTTP {(int)resp.StatusCode}";
                    Log.Error(errMsg, (int)resp.StatusCode);
                    return (false, null, errMsg);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)       // 그 외 오류 로직 
            {
                IsServerOnline = false;
                ShowError("연결 오류");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("서버 연결이 끊겼습니다. 다시 연결을 시도합니다.", "연결 끊김", MessageBoxButton.OK, MessageBoxImage.Error);
                });
                StartConnectionRetryLoop();
                return (false, null, "연결 오류");
            }
            catch (JsonException ex)
            {
                System.Windows.MessageBox.Show($"JSON 파싱 오류가 발생했습니다.:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return (false, null, null);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"알 수 없는 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return (false, null, null);
            }
        }

        private static string ToInvariant(string s)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var d)
               ? d.ToString(CultureInfo.InvariantCulture)
               : s;

        private static bool IsOpToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return Array.Exists(Operators, op => op == value);
        }

        public void Dispose()
        {
            try { SettingsViewModel.SettingsSaved -= OnSettingsSaved; } catch { }
            try { _retryCts.Cancel(); } catch { }
            try { _retryCts.Dispose(); } catch { }
            try { _httpClient?.Dispose(); } catch { }
        }
    }

    public class RelayCommand : ICommand            // Command 부분 구현
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);    
        public void Execute(object? parameter) => _execute(parameter);
    }
}
