using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Net.Http.Headers;
using Calcultor_API.Utils;    // 네 프로젝트의 실제 네임스페이스에 맞게 조정하세요
using Calcultor_API.Models;   // 네 프로젝트의 실제 네임스페이스에 맞게 조정하세요
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

        // ===== 공통: 인증 확인 =====
        private bool TryAuthorize(out IActionResult? unauthorized)
        {
            unauthorized = null;

            // Health 같은 익명 엔드포인트를 호출한 경우(AllowAnonymous로 처리하는 편이 안전)
            // 이 메서드는 인증이 필요한 엔드포인트에서만 호출되도록 사용하세요.

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

        // 숫자 파싱 (culture-안전)
        private static bool TryParseInvariant(string? s, out double value) =>
            double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

        // ===== 실제 연산 공통 처리 =====
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

                // 로깅(예: 콘솔/파일)
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

        // ===== Health endpoint (익명 허용) =====
        // GET /calc/health
        [AllowAnonymous]
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "ok" });
        }

        // ===== 단일 엔드포인트 (권장): op/num1/num2 모두 JSON 바디로 =====
        // POST /calc/compute
        // Body: { "op": "+", "num1": "5", "num2": "6" }
        [HttpPost("compute")]
        public IActionResult Compute([FromBody] CalcRequest? req)
        {
            if (req is null)
                return BadRequest(new { error = "Request body is required." });

            // 유니코드 마이너스(−)가 섞일 수 있어 정규화
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
