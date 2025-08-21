namespace Calcultor_API.Models
{
    // 요청 예: { "op": "+", "num1": "5", "num2": "6" }
    public class CalcRequest
    {
        public string? Op { get; set; }   // "+", "-", "*", "/"
        public string? Num1 { get; set; } // 문자열로 받아 culture-안전 파싱
        public string? Num2 { get; set; }
    }

    // 응답 예: { "op": "+", "num1": "5", "num2": "6", "result": "11" }
    public class CalcResponse
    {
        public string Op { get; set; } = "";
        public string Num1 { get; set; } = "";
        public string Num2 { get; set; } = "";
        public string Result { get; set; } = "";
    }
}
