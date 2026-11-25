using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;
using ProbeDesk.Import;
using ProbeDesk.Import.Export;
using Xunit;

namespace ProbeDesk.Import.Tests;

public class ExportTests
{
    private static FolderNode Tree() => new()
    {
        Name = "Demo",
        Requests =
        {
            new SavedRequest
            {
                Name = "Login",
                Spec = new RequestSpec
                {
                    Method = "POST", Url = "https://x.test/login",
                    Headers = [new("Authorization", "Bearer abcdefghijklmnopqrstuvwxyz"), new("X-A", "1")],
                    BodyType = BodyType.Raw, Body = "{\"password\":\"supersecret123\"}",
                    Auth = new AuthSpec { Kind = AuthKind.Bearer, Token = "fake-secret-token" }
                }
            }
        }
    };

    [Fact]
    public void Postman_export_strips_secrets_and_warns_and_roundtrips()
    {
        var r = Exporter.ToPostman(Tree());
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", r.Content);
        Assert.DoesNotContain("supersecret123", r.Content);
        Assert.DoesNotContain("fake-secret-token", r.Content);
        Assert.NotEmpty(r.Warnings);
        var back = PostmanImporter.Import(r.Content);
        Assert.Equal(1, back.RequestCount);
        Assert.Equal("POST", back.Root.Requests[0].Spec.Method);
    }

    [Fact]
    public void Http_export_strips_secrets_and_roundtrips()
    {
        var r = Exporter.ToHttpFile(Tree());
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", r.Content);
        Assert.DoesNotContain("fake-secret-token", r.Content);
        Assert.Equal(1, HttpFileParser.Parse(r.Content).RequestCount);
    }

    [Fact]
    public void Unstripped_export_keeps_values()
    {
        Assert.Contains("fake-secret-token", Exporter.ToPostman(Tree(), stripSecrets: false).Content);
    }
}
