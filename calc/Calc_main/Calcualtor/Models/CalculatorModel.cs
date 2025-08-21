namespace CalculatorApp.Model
{
    // API 요청을 위한 데이터 모델
    public class CalculationRequest
    {
        public string? Op { get; set; }
        public string? Num1 { get; set; }
        public string? Num2 { get; set; }
    }

    // API 응답을 위한 데이터 모델
    public class CalculationResponse
    {
        public string Result { get; set; } = "";
        public string? Error { get; set; } = "";
    }
    
}