# 0010: Import YAML and zip archives

Status: accepted.

## Decision
- Postman collections and environments may be YAML. `YamlToJson` converts them (YamlDotNet, new dependency in `ProbeDesk.Import`) and the existing JSON importer does the rest. YAML aliases/anchors are rejected to prevent expansion attacks; nesting is capped.
- A zip can hold several files. `ZipImporter` reads it in memory (never to disk), detects each entry by extension (content sniff for unknown), and merges everything into one report with one Save.
- Zip limits: 5000 entries (raised from 200 for large company endpoint archives; the review list collapses when long), 5 MB per entry, 25 MB unpacked (enforced while reading, so a forged size header does not help), 10 MB upload. Unsafe paths, nested zips, hidden/`__MACOSX` entries and unsupported files are skipped and reported.
- OpenAPI/Swagger is out of scope.
