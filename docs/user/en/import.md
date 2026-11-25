# Import

The **Import** page handles:

- **Postman** Collection v2.1 and Postman environment files, as **JSON or YAML** (`.json`, `.yaml`, `.yml`). Postman's newer multi-file YAML export (a folder with `.resources/definition.yaml` and one `*.request.yaml` per request) is supported too: zip the exported folder and import the zip, and the folder structure is kept. Collection variables become an environment. A single definition file only gives that environment, because the requests are separate files. Saved response examples (`*.example.yaml`) are skipped and counted in the report, since they are example responses and not requests.
- **.http / .rest** files (VS Code REST Client, Visual Studio, common httpYac syntax).
- **cURL**: paste a command.
- **Zip archive**: a `.zip` with one or more of the files above. Each file is detected by its extension, everything is merged into one review report and saved with one click. Files of other types, nested zips and files that cannot be read are listed in the report.

Zip limits: up to 5000 files, 5 MB per file, 25 MB unpacked and 10 MB for the zip itself. The archive is read in memory and never unpacked to disk.

## Import report

Everything that cannot be translated is listed in a report, for example Postman scripts, httpYac scripts, `@import` and system variables such as `{{$guid}}`. Nothing is executed and nothing is silently dropped.

## Export

Export a collection as Postman v2.1 or `.http` from the **Collections** page. Secrets are removed by default and you are warned if something looks like a token. Read [How secrets are handled](/help/secrets).

Share by exporting to a file that your colleague imports. Collections are never stored in the repository.
