using ProbeDesk.Core.Http;

namespace ProbeDesk.Core.Tests;

public class CurlBuilderTests
{
    [Fact]
    public void Masks_sensitive_headers_by_default()
    {
        var spec = new RequestSpec
        {
            Method = "post", Url = "https://api.example.test/x", BodyType = BodyType.Raw, Body = "it's",
            Headers = [new("Authorization", "Bearer fake-token"), new("Cookie", "sid=1"), new("Accept", "*/*")]
        };
        var curl = CurlBuilder.Build(spec);
        Assert.DoesNotContain("fake-token", curl);
        Assert.DoesNotContain("sid=1", curl);
        Assert.Contains("'Accept: */*'", curl);
        Assert.Contains("'it'\\''s'", curl);
        Assert.StartsWith("curl -X 'POST'", curl);
    }

    [Fact]
    public void Includes_secrets_only_when_requested() =>
        Assert.Contains("fake-token", CurlBuilder.Build(
            new RequestSpec { Url = "https://a.example.test", Headers = [new("Authorization", "fake-token")] }, true));
}
