# 0005: Local access guard

- `LocalGuardMiddleware` rejects any Host other than localhost/127.0.0.1/[::1] and any Origin whose host or port differs (defends against DNS rebinding and cross-site requests).
- Superseded: an earlier design also required a one-time start token. It was removed because the app is a single-user local tool and loopback binding plus the Host/Origin checks were judged sufficient. Any local process on the machine can therefore reach the app.
- Port is fixed (default 5187, `ProbeDesk:Port`); a random port (0) also works.
