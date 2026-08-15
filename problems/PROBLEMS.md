# PROBLEMS

_Index of resolved bugs. Details in PROBLEMS_DETAILS.md, same number._

1. Logout link never signed out — layout had GET anchor, endpoint is POST-only → replaced with POST form + antiforgery in _Layout (med-history-4ei.11)
2. AddDataProtectionKeys migration recreated Logs table instead of DataProtectionKeys — snapshot mangled during cross-bead rebase; regenerated from known-good snapshot (med-history-nvs.8)
3. Deployed app rendered all dates/times in UTC — AppTime reads TimeZoneInfo.Local and Cloud Run containers default to UTC → ENV TZ=Asia/Bangkok in Dockerfile final stage (med-history-5eu)
