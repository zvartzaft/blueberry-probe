---
name: importers
description: Use when writing or changing Postman, .http/.rest or cURL importers, exporters, the import report, or importer test fixtures.
---
# Importers and exporters

- Live in `src/ProbeDesk.Import`; they produce Core objects (`FolderNode`, `RequestEnvironment`) plus an `ImportReport` (`ImportReport.cs`). Entry points: `PostmanImporter`, `HttpFileParser`, `CurlParser`, `Export/Exporter`.
- Never silently drop content: add a `Warn` for every untranslated item (scripts, unsupported auth, `{{$system}}` variables, `@import`, response handlers). Use `Info` for benign skips (disabled headers).
- Never execute imported scripts.
- YAML is converted to JSON by `YamlToJson` (aliases/anchors rejected) and reuses `PostmanImporter`. Zips go through `ZipImporter`: in memory only, entry/size limits, no nested zips, every skipped file reported; `FileImporter` picks the importer per file.
- Imported file/binary bodies have no file: warn so the user re-picks the file.
- Export strips secrets by default (Authorization/Cookie headers, auth values, token-like text) and returns warnings. Keep `{{var}}` placeholders intact.
- Web import page: `src/ProbeDesk.Web/Components/Pages/Import.razor`; export endpoints in `Program.cs` (`/export/{format}`).
- Fixtures: invented data only, in `tests/ProbeDesk.Import.Tests`.
- `.http` flavors: VS Code REST Client and Visual Studio (`docs/decisions/0003-http-flavor.md`).
