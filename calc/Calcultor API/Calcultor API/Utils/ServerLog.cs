using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Calcultor_API.Utils
{
    public class ServerLog
    {
        private static readonly object _lock = new();
        private static string? _logFilePath;

        public static bool WithTimestamp { get; set; } = true;

        public static void Init(string? filePath = null, bool append = true)
        {
            lock (_lock)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(filePath))
                        filePath = Path.Combine(AppContext.BaseDirectory, "logs", "server_log.txt");

                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    if (!File.Exists(filePath))
                        File.WriteAllText(filePath, "", Encoding.UTF8);

                    _logFilePath = filePath;

                    WriteJson(new
                    {
                        type = "INIT",
                        timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        logFile = _logFilePath
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ServerLog INIT ERROR] {ex.Message}");
                    _logFilePath = null;
                }
            }
        }

        private static void WriteJson(object logObject)
        {
            lock (_lock)
            {
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    string json = JsonSerializer.Serialize(logObject, options);
                    Console.WriteLine(json);
                    if (!string.IsNullOrEmpty(_logFilePath))
                        File.AppendAllText(_logFilePath!, json + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ServerLog WRITE ERROR] {ex.Message}");
                }
            }
        }

        public static void Request(string op, string num1, string num2)
        {
            WriteJson(new
            {
                type = "요청",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                op,
                num1,
                num2
            });
        }

        public static void Response(string result)
        {
            WriteJson(new
            {
                type = "응답",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                result
            });
        }

        public static void Error(string error)
        {
            WriteJson(new
            {
                type = "error",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                error
            });
        }

        public static void Error(string error, int? status = null)
        {
            WriteJson(new
            {
                type = "error",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                error,
                status
            });
        }

        public static void Meta(string url, int statusCode, string? contentType)
        {
            WriteJson(new
            {
                type = "meta",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                url,
                statusCode,
                contentType
            });
        }
    }
}

