using Microsoft.Data.Sqlite;
using ProbeDesk.Core.Http;
using ProbeDesk.Core.Model;

namespace ProbeDesk.Storage.Tests;

internal sealed class FakeProtector : ISecretProtector
{
    public string Protect(string plain) => "ENC:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain));
    public string Unprotect(string p) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(p[4..]));
}

public sealed class StorageFixture : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "pd-test-" + Guid.NewGuid().ToString("N"));
    public string Db => Path.Combine(Dir, "t.db");
    internal CollectionStore Collections { get; }
    internal EnvironmentStore Environments { get; }
    internal HistoryStore History { get; }

    public StorageFixture()
    {
        Directory.CreateDirectory(Dir);
        var f = DbSetup.CreateFactory(Db);
        Collections = new CollectionStore(f, new FakeProtector());
        Environments = new EnvironmentStore(f, new FakeProtector());
        History = new HistoryStore(f);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Dir, true); } catch (IOException) { }
    }
}

public class StorageTests : IClassFixture<StorageFixture>
{
    private readonly StorageFixture _s;
    public StorageTests(StorageFixture s) => _s = s;

    [Fact]
    public async Task Request_roundtrip_rename_duplicate_delete_and_search()
    {
        var r = new SavedRequest
        {
            Name = "Fetch user",
            Spec = new RequestSpec { Method = "PATCH", Url = "https://api.example.test/u", Headers = [new("X-A", "1")] },
            Extractors = [new Extractor { Expression = "x", TargetVariable = "v" }]
        };
        await _s.Collections.SaveRequestAsync(r);

        var loaded = await _s.Collections.GetRequestAsync(r.Id);
        Assert.Equal("PATCH", loaded!.Spec.Method);
        Assert.Equal("X-A", loaded.Spec.Headers[0].Name);
        Assert.Equal("v", loaded.Extractors[0].TargetVariable);

        await _s.Collections.RenameRequestAsync(r.Id, "Ny rubrik");
        var copy = await _s.Collections.DuplicateRequestAsync(r.Id);
        Assert.NotEqual(r.Id, copy!.Id);
        Assert.Equal("Ny rubrik (copy)", copy.Name);
        Assert.Equal(2, (await _s.Collections.SearchAsync("rubrik")).Count);
        Assert.Contains(await _s.Collections.SearchAsync("patch"), x => x.Id == r.Id);

        await _s.Collections.DeleteRequestAsync(r.Id);
        await _s.Collections.DeleteRequestAsync(copy.Id);
        Assert.Null(await _s.Collections.GetRequestAsync(r.Id));
    }

    [Fact]
    public async Task Payload_is_protected_at_rest()
    {
        var r = new SavedRequest { Name = "p", Spec = new RequestSpec { Url = "https://a.example.test", Auth = new AuthSpec { Kind = AuthKind.Bearer, Token = "very-secret-token" } } };
        await _s.Collections.SaveRequestAsync(r);
        await using var conn = new SqliteConnection($"Data Source={_s.Db}");
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Payload FROM Requests WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", r.Id.ToString().ToUpperInvariant());
        var payload = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(payload);
        Assert.DoesNotContain("very-secret-token", payload);
        await _s.Collections.DeleteRequestAsync(r.Id);
    }

    [Fact]
    public async Task Folder_delete_is_recursive_and_import_tree_creates_structure()
    {
        var tree = new FolderNode
        {
            Name = "root",
            Requests = [new SavedRequest { Name = "top", Spec = new RequestSpec { Url = "https://a.example.test" } }],
            Folders = [new FolderNode { Name = "Auth", Requests = [new SavedRequest { Name = "login", Spec = new RequestSpec { Url = "https://a.example.test/l" } }] }]
        };
        var parent = await _s.Collections.CreateFolderAsync("Importerat", null);
        await _s.Collections.ImportTreeAsync(tree, parent.Id);

        var exported = await _s.Collections.ExportTreeAsync(parent.Id);
        Assert.Single(exported.Requests);
        Assert.Equal("Auth", exported.Folders[0].Name);
        Assert.Equal("login", exported.Folders[0].Requests[0].Name);

        await _s.Collections.DeleteFolderAsync(parent.Id);
        Assert.DoesNotContain(await _s.Collections.ListFoldersAsync(), f => f.Id == parent.Id || f.Name == "Auth");
        Assert.DoesNotContain(await _s.Collections.ListRequestsAsync(), r => r.Name is "top" or "login");
    }

    [Fact]
    public async Task Secret_variables_keep_value_when_saved_blank()
    {
        var env = new RequestEnvironment
        {
            Name = "Test", IsProduction = true,
            Variables = [new() { Name = "key", Value = "s3", IsSecret = true }, new() { Name = "p", Value = "pub" }]
        };
        await _s.Environments.SaveAsync(env);

        var edited = (await _s.Environments.GetAsync(env.Id))!;
        edited.Variables.First(v => v.Name == "key").Value = "";
        await _s.Environments.SaveAsync(edited);

        var again = (await _s.Environments.GetAsync(env.Id))!;
        Assert.Equal("s3", again.Variables.First(v => v.Name == "key").Value);
        Assert.True(again.IsProduction);
        await _s.Environments.DeleteAsync(env.Id);
    }

    [Fact]
    public async Task Environment_with_duplicate_variable_names_can_be_saved_again()
    {
        var env = new RequestEnvironment
        {
            Name = "Dup",
            Variables = [new() { Name = "a", Value = "1" }, new() { Name = "a", Value = "2" }]
        };
        await _s.Environments.SaveAsync(env);
        await _s.Environments.SaveAsync(env);
        await _s.Environments.DeleteAsync(env.Id);
    }

    [Fact]
    public void History_paths_redact_ids_and_tokens()
    {
        Assert.Equal("/api/users/:id/orders/:id", HistoryStore.Redact("/api/users/42/orders/3f2504e0-4f89-11d3-9a0c-0305e82c3301"));
        Assert.Equal("/api/users/me", HistoryStore.Redact("/api/users/me"));
    }

    [Fact]
    public async Task Session_cookies_are_stored_with_the_environment()
    {
        var env = new RequestEnvironment
        {
            Name = "Cookies", BaseUrl = "https://example.test", AutoCsrf = true,
            SessionCookies = [new(".AspNetCore.Cookies", "fake-auth")]
        };
        await _s.Environments.SaveAsync(env);

        var again = (await _s.Environments.GetAsync(env.Id))!;
        Assert.True(again.AutoCsrf);
        Assert.Equal("fake-auth", Assert.Single(again.SessionCookies).Value);
        await _s.Environments.DeleteAsync(env.Id);
    }

    [Fact]
    public async Task History_keeps_metadata_only_without_query()
    {
        await _s.History.AddAsync("get", "https://api.example.test/a/b?token=abc", 200, 12);
        var h = (await _s.History.ListAsync()).First();
        Assert.Equal("GET", h.Method);
        Assert.Equal("api.example.test", h.Host);
        Assert.Equal("/a/b", h.Path);
        Assert.DoesNotContain("token", h.Path + h.Host);
        await _s.History.ClearAsync();
        Assert.Empty(await _s.History.ListAsync());
    }

    [Fact]
    public async Task Uploads_enforce_limit_and_cleanup_removes_unreferenced()
    {
        var store = new UploadStore(Path.Combine(_s.Dir, "up"), maxBytes: 10);
        await Assert.ThrowsAsync<UploadTooLargeException>(() => store.SaveAsync(new MemoryStream(new byte[11]), "big.bin"));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_s.Dir, "up")));

        var kept = await store.SaveAsync(new MemoryStream(new byte[5]), "../evil/keep.txt");
        var gone = await store.SaveAsync(new MemoryStream(new byte[5]), "gone.txt");
        Assert.Equal("keep.txt", Path.GetFileName(kept));

        var removed = store.Cleanup(new HashSet<string> { kept }, TimeSpan.Zero);
        Assert.Equal(1, removed);
        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(gone));
    }
}
