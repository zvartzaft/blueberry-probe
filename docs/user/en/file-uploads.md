# File uploads

On the **Body** tab choose **Multipart form** for form fields with files, or **Binary file** for a request where the whole body is a file (for example PUT).

- Files are copied into Blueberry Probe's local data folder and the request points at the copy.
- The size is limited (50 MB by default).
- Files no longer used by any saved request are cleaned up at startup.

Try it against `/_echo/upload`, which shows the file name and size.

See also [Getting started](/help/getting-started).
