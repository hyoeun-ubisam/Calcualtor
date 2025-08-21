namespace Calcultor_API.Models
{
    public class CalcRequest
    {
        public string? Op { get; set; }   
        public string? Num1 { get; set; } 
        public string? Num2 { get; set; }
    }

    public class CalcResponse
    {
        public string Op { get; set; } = "";
        public string Num1 { get; set; } = "";
        public string Num2 { get; set; } = "";
        public string Result { get; set; } = "";
    }
}
