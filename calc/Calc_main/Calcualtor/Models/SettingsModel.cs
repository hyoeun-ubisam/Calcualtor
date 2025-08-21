using System;

namespace Calculator.Models
{
    // 설정 데이터를 저장 및 관리
    public class SettingsData
    {
        public string ServerAddress { get; set; } = "https://localhost";
        public int ServerPort { get; set; } = 5001;
        public string? Token { get; set; }

        public string BaseUrl
        {
            get
            {
                if (Uri.TryCreate(ServerAddress, UriKind.Absolute, out var u) &&
                    (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
                {
                    var builder = new UriBuilder(u.Scheme, u.Host, ServerPort);
                    return EnsureTrailingSlash(builder.Uri.AbsoluteUri);
                }
                else
                {
                    var builder = new UriBuilder("https", ServerAddress, ServerPort);
                    return EnsureTrailingSlash(builder.Uri.AbsoluteUri);
                }
            }
        }

        private static string EnsureTrailingSlash(string url) => url.EndsWith("/") ? url : url + "/";
    }

    // 설정을 수정하고 저장하면, 발생하는 이벤트
    public class SettingsSavedEventArgs : EventArgs
    {
        public string BaseUrl { get; init; } = "";
        public string? Token { get; init; }
    }

}
