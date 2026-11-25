using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Core.Flow;

public sealed class MissingVariableException(IReadOnlyList<string> names)
    : InvalidOperationException("Variables are not defined: " + string.Join(", ", names))
{
    public IReadOnlyList<string> Names { get; } = names;
}

public sealed class FlowException(string message) : InvalidOperationException(message);

public sealed record StepResult(string Name, ResponseResult Response,
    IReadOnlyList<string> Extracted, IReadOnlyList<AssertionResult> Assertions);

public sealed record FlowResult(IReadOnlyList<StepResult> Steps)
{
    public StepResult Final => Steps[^1];
}

public sealed record SequenceItem(string Name, StepResult? Final, Exception? Error);

/// <summary>Runs saved requests (with their RunBefore steps) in one session, sharing variables.</summary>
public sealed class FlowRunner(HttpSession session, RequestEnvironment? env = null, string? spoolDirectory = null, string? uploadRoot = null)
{
    private const int MaxDepth = 10;

    private HttpExecutor NewExecutor() => new(session, env?.AllowedHosts, spoolDirectory, uploadRoot);

    public Dictionary<string, string> CurrentVariables()
    {
        var vars = VariableResolver.ToMap(env);
        foreach (var kv in session.Variables) vars[kv.Key] = kv.Value;
        return vars;
    }

    /// <summary>Ordered list of requests that will run, main request last.</summary>
    public static List<SavedRequest> Plan(SavedRequest main, Func<Guid, SavedRequest?> lookup)
    {
        var plan = new List<SavedRequest>();
        var stack = new HashSet<Guid>();
        void Visit(SavedRequest r, int depth)
        {
            if (depth > MaxDepth) throw new FlowException("Flow is nested too deeply.");
            if (!stack.Add(r.Id)) throw new FlowException("Flow contains a cycle.");
            foreach (var id in r.RunBefore)
            {
                var dep = lookup(id) ?? throw new FlowException("A step in the flow no longer exists.");
                if (plan.All(p => p.Id != dep.Id)) Visit(dep, depth + 1);
            }
            stack.Remove(r.Id);
            if (plan.All(p => p.Id != r.Id)) plan.Add(r);
        }
        Visit(main, 0);
        return plan;
    }

    public async Task<FlowResult> RunAsync(SavedRequest main, Func<Guid, SavedRequest?> lookup, CancellationToken ct = default)
    {
        var steps = new List<StepResult>();
        foreach (var step in Plan(main, lookup))
            steps.Add(await RunStepAsync(step, ct));
        return new FlowResult(steps);
    }

    public async Task<IReadOnlyList<SequenceItem>> RunSequenceAsync(
        IEnumerable<SavedRequest> requests, Func<Guid, SavedRequest?> lookup, CancellationToken ct = default)
    {
        var items = new List<SequenceItem>();
        foreach (var r in requests)
        {
            ct.ThrowIfCancellationRequested();
            try { items.Add(new(r.Name, (await RunAsync(r, lookup, ct)).Final, null)); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            { items.Add(new(r.Name, null, ex)); }
        }
        return items;
    }

    /// <summary>Resolves variables. Masked mode hides secrets and extracted values (for previews).</summary>
    public RequestSpec Resolve(RequestSpec spec, bool masked)
    {
        var vars = masked ? VariableResolver.ToMaskedMap(env) : CurrentVariables();
        if (masked) foreach (var k in session.Variables.Keys) vars[k] = "***";
        return VariableResolver.Resolve(spec, vars, env?.BaseUrl ?? "");
    }

    public async Task<StepResult> RunStepAsync(SavedRequest step, CancellationToken ct = default)
    {
        var vars = CurrentVariables();
        var missing = VariableResolver.Missing(step.Spec, vars).ToList();
        if (missing.Count > 0) throw new MissingVariableException(missing);

        var spec = VariableResolver.Resolve(step.Spec, vars, env?.BaseUrl ?? "");
        await ApplyOAuthAsync(spec, ct);
        await ApplyCsrfAsync(spec, ct);
        var response = await NewExecutor().SendAsync(spec, ct);

        var extracted = new List<string>();
        foreach (var ex in step.Extractors.Where(e => e.TargetVariable.Length > 0))
        {
            var value = ExtractorEngine.Extract(ex, response, session.Cookies);
            if (value is null) continue;
            session.Variables[ex.TargetVariable] = value;
            extracted.Add(ex.TargetVariable);
        }
        return new StepResult(step.Name, response, extracted, AssertionEngine.EvaluateAll(step.Assertions, response));
    }

    // The token is only attached for requests to the environment's own origin, never to other hosts.
    private async Task ApplyCsrfAsync(RequestSpec spec, CancellationToken ct)
    {
        if (env is not { AutoCsrf: true } || !CsrfTokenProvider.IsMutating(spec.Method)) return;
        if (!Uri.TryCreate(env.BaseUrl, UriKind.Absolute, out var baseUri) ||
            !Uri.TryCreate(spec.Url, UriKind.Absolute, out var target) ||
            target.Scheme != baseUri.Scheme || target.Port != baseUri.Port ||
            !target.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase)) return;
        if (spec.Headers.Any(h => h.Name.Equals(SessionCookies.CsrfHeader, StringComparison.OrdinalIgnoreCase))) return;

        var token = await CsrfTokenProvider.FetchAsync(NewExecutor(), baseUri, ct);
        spec.Headers = [.. spec.Headers, new HeaderEntry(SessionCookies.CsrfHeader, token)];
    }

    private async Task ApplyOAuthAsync(RequestSpec spec, CancellationToken ct)
    {
        var a = spec.Auth;
        if (a.Kind != AuthKind.OAuth2ClientCredentials) return;
        var key = a.TokenUrl + "|" + a.ClientId + "|" + a.Scope;
        if (!session.OAuthTokens.TryGetValue(key, out var cached) || cached.Expires <= DateTimeOffset.UtcNow)
        {
            var (token, exp) = await OAuth2ClientCredentials.FetchAsync(NewExecutor(), a, ct);
            cached = (token, DateTimeOffset.UtcNow.AddSeconds(Math.Max(10, (exp ?? 300) - 30)));
            session.OAuthTokens[key] = cached;
        }
        spec.Auth = new AuthSpec { Kind = AuthKind.Bearer, Token = cached.Token };
    }
}
