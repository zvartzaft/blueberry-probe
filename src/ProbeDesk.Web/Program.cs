using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using ProbeDesk.Core.Http;
using ProbeDesk.Import.Export;
using ProbeDesk.Storage;
using ProbeDesk.Web.Components;
using ProbeDesk.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Security rule 1: bind to loopback only.
var port = builder.Configuration.GetValue("ProbeDesk:Port", 0);
builder.WebHost.ConfigureKestrel(o => o.ListenLocalhost(port));
// Framework scripts (blazor.web.js) live outside wwwroot; load the asset manifest in every environment.
builder.WebHost.UseStaticWebAssets();

var dataDir = builder.Configuration["ProbeDesk:DataDir"];
var paths = string.IsNullOrWhiteSpace(dataDir) ? AppPaths.Default() : new AppPaths(dataDir);
paths.Ensure();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddSingleton(paths);
if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("Secret storage currently requires Windows (DPAPI).");
builder.Services.AddSingleton<ProbeDesk.Core.Model.ISecretProtector, DpapiSecretProtector>();
builder.Services.AddSingleton(DbSetup.CreateFactory(paths.Database));
builder.Services.AddSingleton<CollectionStore>();
builder.Services.AddSingleton<EnvironmentStore>();
builder.Services.AddSingleton<HistoryStore>();
builder.Services.AddSingleton(new UploadStore(paths.Uploads));
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton<HelpService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseMiddleware<LocalGuardMiddleware>();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapEcho();

// Exports are always stripped: unstripped secrets must never travel over a plain GET.
app.MapGet("/export/{format}", async (string format, Guid? folder, CollectionStore store) =>
{
    if (format is not ("http" or "postman")) return Results.NotFound();
    var tree = await store.ExportTreeAsync(folder);
    if (tree.Name.Length == 0) tree.Name = "Blueberry Probe";
    var result = format == "http" ? Exporter.ToHttpFile(tree, true) : Exporter.ToPostman(tree, true);
    var file = format == "http" ? "blueberry-probe.http" : "blueberry-probe.postman_collection.json";
    return Results.File(System.Text.Encoding.UTF8.GetBytes(result.Content), "application/octet-stream", file);
});

app.MapGet("/download/spool/{name}", (string name, AppPaths p) =>
{
    // Spool files are always named <32 hex chars>.bin; reject everything else.
    if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9a-f]{32}\\.bin$")) return Results.NotFound();
    var path = Path.Combine(Path.GetFullPath(p.Spool), name);
    return File.Exists(path) ? Results.File(path, "application/octet-stream", "response.bin") : Results.NotFound();
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
    var baseUrl = addresses.FirstOrDefault(a => a.Contains("127.0.0.1")) ?? addresses.FirstOrDefault() ?? $"http://127.0.0.1:{port}";
    baseUrl = baseUrl.Replace("localhost", "127.0.0.1");

    Console.WriteLine($"Blueberry Probe: {baseUrl}");

    _ = Task.Run(async () =>
    {
        try
        {
            var collections = app.Services.GetRequiredService<CollectionStore>();
            await Seeder.SeedAsync(collections, app.Services.GetRequiredService<EnvironmentStore>(), baseUrl);

            // Startup cleanup: unreferenced uploads and spooled responses (full response bodies must not linger on disk).
            app.Services.GetRequiredService<UploadStore>().Cleanup(await collections.GetReferencedFilePathsAsync(), TimeSpan.FromDays(1));
            foreach (var f in Directory.EnumerateFiles(paths.Spool))
            {
                try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogError("Startup tasks failed: {Type}", ex.GetType().Name);
        }
    });
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    try { foreach (var f in Directory.EnumerateFiles(paths.Spool)) File.Delete(f); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
});

app.Run();

public partial class Program;
