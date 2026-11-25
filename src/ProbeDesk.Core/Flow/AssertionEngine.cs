using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Core.Flow;

public sealed record AssertionResult(Assertion Assertion, bool Passed, string? Actual);

public static class AssertionEngine
{
    public static AssertionResult Evaluate(Assertion a, ResponseResult r)
    {
        switch (a.Kind)
        {
            case AssertionKind.Status:
                var actual = r.StatusCode.ToString();
                var exp = a.Expected.Trim();
                var ok = exp.Length == 3 && exp.EndsWith("xx", StringComparison.OrdinalIgnoreCase)
                    ? actual[0] == exp[0] : actual == exp;
                return new(a, ok, actual);
            case AssertionKind.Header:
                var h = r.Headers.FirstOrDefault(x => x.Name.Equals(a.Expression, StringComparison.OrdinalIgnoreCase))?.Value;
                return new(a, h is not null && (a.Expected.Length == 0 || h == a.Expected), h);
            default:
                var v = JsonPathLite.Select(r.Body, a.Expression);
                return new(a, v is not null && (a.Expected.Length == 0 || v == a.Expected), v);
        }
    }

    public static List<AssertionResult> EvaluateAll(IEnumerable<Assertion> assertions, ResponseResult r) =>
        assertions.Select(a => Evaluate(a, r)).ToList();
}
