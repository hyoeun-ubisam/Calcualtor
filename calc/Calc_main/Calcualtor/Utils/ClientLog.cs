using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Calculator.Utils
{
    public static class ClientLog
    {
        private static readonly object _lock = new();
        private static string? _logFilePath;

        public static void Init(string? filePath = null)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "logs", "Client_log.txt");

                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                _logFilePath = filePath;
            }
        }

        private static void CalJson(object logObject)
        {
            lock (_lock)
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string json = JsonSerializer.Serialize(logObject, options);
                if (!string.IsNullOrEmpty(_logFilePath))
                    File.AppendAllText(_logFilePath!, json + Environment.NewLine, Encoding.UTF8);
            }
        }

        public static void Request(string op, string num1, string num2)
        {
            CalJson(new
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
            CalJson(new
            {
                type = "응답",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                result
            });
        }

        public static void Error(string error, int? status = null)
        {
            CalJson(new
            {
                type = "에러",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                error,
                status
            });
        }

        public static void Meta(string url, int statusCode, string? contentType)
        {
            CalJson(new
            {
                type = "meta",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                url,
                status = statusCode,
                contentType
            });
        }
    }
}