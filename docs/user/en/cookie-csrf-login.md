# Cookie and CSRF login

There are two ways to get a signed-in session: paste the cookies from your browser, or log in with request steps.

## Paste cookies from your browser

1. In **Environments**, set the **Base URL** to the system's API host (for example `https://flows.example.test`). Only the scheme and host are used. Tick **Add CSRF token automatically** if you send POST, PUT, PATCH or DELETE, then save.
2. Sign in to the system in your browser. Open the developer tools (F12), the **Network** tab, click a request to that host and copy the whole value of the `Cookie` request header.
3. Select the environment in the left navigation, open **Cookies**, paste into **Cookie header** and click **Save cookies**.
4. Click **Test session**. It calls `/api/users/me` on the Base URL's host. A 2xx answer means the session works. A 401, 403 or redirect means it has expired, and a 404 usually means the Base URL is not the right host.

What happens next:

- Blueberry Probe keeps only the `.AspNetCore.Cookies` (auth) and `.AspNetCore.Antiforgery.*` cookies and ignores the rest. Pasting again replaces them.
- They are stored encrypted on your computer, per environment, and are never shown again. The page only shows whether they are set. **Clear cookies** removes them.
- They are sent automatically with every request to the Base URL's host, also after a restart. Cookies the server refreshes during a run are saved too.
- With **Add CSRF token automatically**, Blueberry Probe fetches a token from `/api/authentication/antiforgery/token` before each POST, PUT, PATCH or DELETE to the Base URL's host and sends it as `X-CSRF-TOKEN`. It never sends it to other hosts. If you set an `X-CSRF-TOKEN` header yourself, yours is used.
- When the session expires, paste a new `Cookie` header.
- **Start a new session** clears the variables extracted from responses and reloads the stored cookies.

## Log in with request steps

Many systems require you to fetch a login page first, pick out a token and then send it with the login. In Blueberry Probe this becomes a single click using **flow steps**. Cookies the server sets during the flow are kept in memory for the session.

### Step by step (using the built-in echo endpoint)

1. **Fetch the login page:** `GET /_echo/login-page`. The server sets the `XSRF-TOKEN` cookie and shows a token in a hidden field.
2. **Extract the token:** on step 1, open the **Flow** tab and add an extractor. Source *Response body*, match *CSS selector*, expression `input[name=__RequestVerificationToken]@value`, variable `csrf`.
3. **Log in:** `POST /_echo/login` with the form field `__RequestVerificationToken` = `{{csrf}}`. The cookie is sent automatically.
4. The response is a redirect (302). Turn off *Follow redirects* under **Settings** if you want to see `Set-Cookie`. The **Cookies** tab of the response lists the cookies the server set.

Add step 1 under **Flow** on the login request, so it runs first every time.

### Extractors

| Source | Match | Example |
|---|---|---|
| Response body | Regex | `token=(\w+)` |
| Response body | JSONPath | `$.data.token` |
| Response body | CSS selector | `input[name=csrf]@value` |
| Response header | By name | `X-Request-Id` |
| Cookie | By name | `XSRF-TOKEN` |

To send a cookie as a header (for example `XSRF-TOKEN` as `X-XSRF-TOKEN`), extract the cookie into a variable and use `{{variable}}` in the header.
