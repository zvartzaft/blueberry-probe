using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;
using ProbeDesk.Storage;

namespace ProbeDesk.Web.Services;

/// <summary>First-run example data pointing at the built-in echo endpoint (fake data only).</summary>
public static class Seeder
{
    public static async Task SeedAsync(CollectionStore collections, EnvironmentStore envs, string baseUrl)
    {
        if (!await collections.IsEmptyAsync()) return;

        await envs.SaveAsync(new RequestEnvironment
        {
            Name = "Example (local echo)",
            BaseUrl = baseUrl,
            Variables = [new Variable { Name = "user", Value = "demo" }],
            AllowedHosts = ["127.0.0.1", "localhost"]
        });

        var folder = await collections.CreateFolderAsync("Examples", null);

        var hello = new SavedRequest
        {
            Name = "Echo: hello",
            FolderId = folder.Id,
            Spec = new RequestSpec { Method = "GET", Url = "/_echo/hello?name={{user}}" }
        };
        var page = new SavedRequest
        {
            Name = "1. Fetch login page",
            FolderId = folder.Id,
            Spec = new RequestSpec { Method = "GET", Url = "/_echo/login-page" },
            Extractors = [new Extractor
            {
                Source = ExtractSource.Body, Kind = ExtractKind.Css,
                Expression = "input[name=__RequestVerificationToken]@value", TargetVariable = "csrf"
            }]
        };
        var login = new SavedRequest
        {
            Name = "2. Log in (cookie + CSRF)",
            FolderId = folder.Id,
            RunBefore = [page.Id],
            Spec = new RequestSpec
            {
                Method = "POST", Url = "/_echo/login", BodyType = BodyType.FormUrlEncoded,
                FormFields =
                [
                    new FormField { Name = "__RequestVerificationToken", Value = "{{csrf}}" },
                    new FormField { Name = "username", Value = "{{user}}" },
                    new FormField { Name = "password", Value = "example" }
                ],
                FollowRedirects = true
            },
            Assertions = [new Assertion { Kind = AssertionKind.Status, Expected = "200" }]
        };
        foreach (var r in new[] { hello, page, login }) await collections.SaveRequestAsync(r);
    }
}
