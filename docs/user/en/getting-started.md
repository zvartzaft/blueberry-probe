# Getting started

Blueberry Probe is a local tool for sending HTTP requests. Everything is stored on your own computer and nothing is shared automatically.

## Try it without risk

On first start there is an example environment and an example collection that point at the built-in echo endpoint `/_echo`. It answers with what you sent, so you can practice without touching real systems.

1. Open the **Workspace** and type a URL, or pick a saved request in the list on the left.
2. Press **Send** or `Ctrl+Enter`.
3. Read the response, and check the **Preview** tab to see the final request.

To name or rename a saved request, select **Rename** beside it in the left-hand list. You can also edit its name above the request and press **Save**.

## Use it against your own system

1. Open **Environments**, create an environment and set its **Base URL** (for example `https://flows.example.test`). Save it and select it with the **Environment** selector at the top of the left navigation.
2. If the system needs a browser sign-in, open **Cookies** and paste your browser's `Cookie` header. Use **Test session** to check it. See [Cookie and CSRF login](/help/cookie-csrf-login).
3. Send requests from the **Workspace**. Relative URLs such as `/api/users/me` get the Base URL put in front of them.

## Next steps

- [Variables and environments](/help/variables-environments)
- [Cookie and CSRF login](/help/cookie-csrf-login)
- [File uploads](/help/file-uploads)
- [Import](/help/import)
- [How secrets are handled](/help/secrets)
- [Keyboard shortcuts](/help/shortcuts)
- [What's new](/help/whats-new)
