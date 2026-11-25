# How secrets are handled

- Secret variables are never shown again after saving and are only resolved on the server when the request is sent.
- Stored data is protected with Windows DPAPI (tied to your user account).
- `Authorization`, `Cookie` and `Set-Cookie` values are never written to logs, history or exports.
- Responses are not stored. History only contains method, host, path, status and time (without query parameters).
- Session cookies pasted on the **Cookies** page are stored encrypted per environment (only the auth and antiforgery cookies) and never shown again. **Clear cookies** deletes them. Cookies set during a flow stay in memory only.
- Treat a copied `Cookie` header like a password: do not share it or save it in files.
- The app only listens on `127.0.0.1` and rejects requests with a foreign host or origin.
- Certificates are always validated unless you explicitly turn that off for an environment.

See [Variables and environments](/help/variables-environments).
