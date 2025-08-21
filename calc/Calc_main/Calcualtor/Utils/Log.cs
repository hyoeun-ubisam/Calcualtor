using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using log4net;

namespace Calculator.Utils
{
    public static class Log
    {
        public static bool Enabled { get; set; } = true;
        public static bool WithTimestamp { get; set; } = true;

        static readonly object _lock = new();
        static TextWriter? _file;
        public static ILog? log { get; set; } = LogManager.GetLogger(typeof(Log));


        public static bool InitFile(string? filePath = null, bool append = true)
        {
            lock (_lock)
            {
                try
                {
                    if (_file != null)
                    {
                        Debug.WriteLine("[Log] InitFile: already initialized.");
                        return true;
                    }

                    string? tryPath = filePath;

                    // if no path provided, pick LocalApplicationData
                    if (string.IsNullOrWhiteSpace(tryPath))
                    {
                        tryPath = GetDefaultLogPath();
                        Debug.WriteLine($"[Log] InitFile: no path provided, using default: {tryPath}");
                    }

                    // Ensure folder exists (if possible). If Path.GetDirectoryName returns null, fallback.
                    var dir = Path.GetDirectoryName(tryPath);
                    if (string.IsNullOrWhiteSpace(dir))
                    {
                        Debug.WriteLine($"[Log] InitFile: invalid directory from path '{tryPath}', switching to LocalApplicationData fallback.");
                        tryPath = GetDefaultLogPath();
                        dir = Path.GetDirectoryName(tryPath);
                    }

                    Directory.CreateDirectory(dir!);

                    // Try to open file
                    _file = new StreamWriter(tryPath!, append, Encoding.UTF8) { AutoFlush = true };
                    Debug.WriteLine($"[Log] InitFile: log file opened at '{tryPath}'");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[Log] InitFile failed: " + ex.ToString());

                    // Try fallback to LocalApplicationData
                    try
                    {
                        var fallback = GetDefaultLogPath();
                        var fallbackDir = Path.GetDirectoryName(fallback);
                        if (!string.IsNullOrWhiteSpace(fallbackDir))
                            Directory.CreateDirectory(fallbackDir);

                        _file = new StreamWriter(fallback, append, Encoding.UTF8) { AutoFlush = true };
                        Debug.WriteLine($"[Log] InitFile: fallback log file opened at '{fallback}'");
                        return true;
                    }
                    catch (Exception ex2)
                    {
                        Debug.WriteLine("[Log] InitFile fallback failed: " + ex2.ToString());
                        _file = null;
                        return false;
                    }
                }
            }
        }

        static string GetDefaultLogPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(appData, "CalculatorApp", "logs");
            Directory.CreateDirectory(folder);
            var fname = $"client_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            return Path.Combine(folder, fname);
        }

        static void CalJson(object obj)
        {
            lock (_lock)
            {
                if (!Enabled) return;

                try
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    string json = JsonSerializer.Serialize(obj, options);

                    Debug.WriteLine(json);
                    if (_file != null)
                    {
                        _file.WriteLine(json);
                    }
                    else
                    {
                        Debug.WriteLine("[Log] _file is null, skipping write.");
                    }
                    log?.Info(json);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[Log] CalJson failed: " + ex.ToString());
                }
            }
        }

        public static void Request(string op, string num1, string num2)
        {
            CalJson(new
            {
                type = "request",
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
                type = "response",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                result
            });
        }

        public static void Error(string error, int? status = null)
        {
            CalJson(new
            {
                type = "error",
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

        // Optional: call on exit to close file handle cleanly
        public static void Close()
        {
            lock (_lock)
            {
                try { _file?.Flush(); } catch { }
                try { _file?.Dispose(); } catch { }
                _file = null;
            }
        }
    }
}
