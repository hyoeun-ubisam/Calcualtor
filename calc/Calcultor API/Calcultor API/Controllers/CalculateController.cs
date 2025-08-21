using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Net.Http.Headers;
using Calcultor_API.Utils;   
using Calcultor_API.Models;   
using Microsoft.Extensions.Logging;
using System;

namespace Calcultor_API.Controllers
{
    [ApiController]
    [Route("calc")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public class CalculateController : ControllerBase
    {
        private readonly ILogger<CalculateController> _logger;
        private const string ValidToken = "secret_token_123";

        public CalculateController(ILogger<CalculateController> logger) => _logger = logger;

        //인증 확인
        private bool TryAuthorize(out IActionResult? unauthorized)
        {
            unauthorized = null;

            if (!Request.Headers.TryGetValue("Authorization", out var authValue) ||
                !AuthenticationHeaderValue.TryParse(authValue, out var authHeader) ||
                !string.Equals(authHeader.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(authHeader.Parameter))
            {
                unauthorized = Unauthorized(new { error = "Authorization header missing or malformed" });
                return false;
            }
            if (!string.Equals(authHeader.Parameter, ValidToken, StringComparison.Ordinal))
            {
                unauthorized = Unauthorized(new { error = "Invalid token" });
                return false;
            }
            return true;
        }

        // 숫자 파싱 
        private static bool TryParseInvariant(string? s, out double value) =>
            double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

        // 연산 공통 처리 
        private IActionResult DoOpCore(string endpoint, string symbol, string? num1Str, string? num2Str)
        {
            if (!TryAuthorize(out var unauth)) return unauth!;

            if (string.IsNullOrWhiteSpace(num1Str) || string.IsNullOrWhiteSpace(num2Str))
                return BadRequest(new { error = "num1 and num2 are required." });

            if (!TryParseInvariant(num1Str, out var a) || !TryParseInvariant(num2Str, out var b))
                return BadRequest(new { error = "Invalid number." });

            if (symbol == "/" && b == 0)
                return BadRequest(new { error = "Cannot divide by zero." });

            try
            {
                double r = symbol switch
                {
                    "+" => a + b,
                    "-" => a - b,
                    "*" => a * b,
                    "/" => a / b,
                    _ => throw new InvalidOperationException("Unsupported operator")
                };

                var result = r.ToString(CultureInfo.InvariantCulture);

                // 로그 기록(응답, 요청)
                ServerLog.Request(symbol, num1Str!, num2Str!);
                ServerLog.Response(result);

                var response = new CalcResponse
                {
                    Op = symbol,
                    Num1 = a.ToString(CultureInfo.InvariantCulture),
                    Num2 = b.ToString(CultureInfo.InvariantCulture),
                    Result = result
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                ServerLog.Error(ex.Message);
                _logger.LogWarning(ex, "Compute failed on {Endpoint} ({Num1},{Num2})", endpoint, num1Str, num2Str);
                return BadRequest(new { error = "Invalid expression" });
            }
        }

        // GET /calc/health
        [AllowAnonymous]
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "ok" });
        }

        // POST(/calc/compute) 방식, REST API에서는 POST 방식이 일반적이라고 하여 POST로 구현을 해 봤습니다.

        [HttpPost("compute")]
        public IActionResult Compute([FromBody] CalcRequest? req)
        {
            if (req is null)
                return BadRequest(new { error = "Request body is required." });

            var op = (req.Op ?? "").Trim().Replace('−', '-');

            if (op is not ("+" or "-" or "*" or "/"))
                return BadRequest(new { error = "Unsupported operator. Use one of +, -, *, /" });

            return DoOpCore("/calc/compute", op, req.Num1, req.Num2);
        }

        [HttpPost("add")]
        public IActionResult Add([FromBody] CalcRequest? req)
            => req is null ? BadRequest(new { error = "Request body is required." })
                           : DoOpCore("/calc/add", "+", req.Num1, req.Num2);

        [HttpPost("sub")]
        public IActionResult Sub([FromBody] CalcRequest? req)
            => req is null ? BadRequest(new { error = "Request body is required." })
                           : DoOpCore("/calc/sub", "-", req.Num1, req.Num2);

        [HttpPost("mul")]
        public IActionResult Mul([FromBody] CalcRequest? req)
            => req is null ? BadRequest(new { error = "Request body is required." })
                           : DoOpCore("/calc/mul", "*", req.Num1, req.Num2);

        [HttpPost("div")]
        public IActionResult Div([FromBody] CalcRequest? req)
            => req is null ? BadRequest(new { error = "Request body is required." })
                           : DoOpCore("/calc/div", "/", req.Num1, req.Num2);
    }
}
