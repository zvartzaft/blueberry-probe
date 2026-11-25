# Variables and environments

Write `{{name}}` in the URL, headers, query parameters, body and form fields. The value is inserted when the request is sent.

## Environments

An environment has a name, a base URL and variables. Relative URLs (such as `/login`) get the base URL put in front of them. The base URL is also available as `{{baseUrl}}` (used by imported Postman collections), unless you define a variable named `baseUrl` yourself. Switch environments with the **Environment** selector at the top of the left navigation. The list updates when you save, delete or import environments.

- **Production:** mark the environment as production. A clear warning is shown at the top and you must confirm before POST, PUT, PATCH and DELETE requests are sent.
- **Ignore certificate errors:** off by default. Only turn it on for a single environment you trust.
- **Add CSRF token automatically:** fetches a CSRF token and sends it as `X-CSRF-TOKEN` on POST, PUT, PATCH and DELETE to the Base URL's host. See [Cookie and CSRF login](/help/cookie-csrf-login).
- **Allowed hosts:** an optional list (for example `api.example.test` or `*.example.test`). Requests to other hosts are blocked.
- **Session cookies:** pasted on the **Cookies** page and stored with the environment. They belong to the host in the Base URL, so set the Base URL first.

## Secret variables

Mark a variable as secret. After saving it is never shown again. Leave the field empty to keep the stored value. See [How secrets are handled](/help/secrets).

## Values from responses

With **extractors** a value from a response can be saved into a variable. Read more in [Cookie and CSRF login](/help/cookie-csrf-login).
