# Config Studio web

DAQ pages in this folder talk to the single Host at `/api/v1`. The application shell (sidebar, header, sign-in card, theme drawer, Tailwind setup) is adapted from [shadcn-admin](https://github.com/satnaing/shadcn-admin) v2.2.1 by [Sat Naing](https://github.com/satnaing).

That template is MIT licensed. A copy of its license is [LICENSE](LICENSE). Demo: https://shadcn-admin.netlify.app/

This repository's product code stays under the parent [Apache-2.0](../../LICENSE) license. The vendored shell keeps the MIT notice.

## Scripts

CI uses npm. The upstream template uses pnpm; either installer works if the lockfile you generate matches.

```bash
npm install
npm run dev    # http://127.0.0.1:5173 , proxies /api to :5080
npm run build  # writes dist/ served by src/Host
```

How to run Host, Api, and this UI together is in the repository [README](../../README.md).
