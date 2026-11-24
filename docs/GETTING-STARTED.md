# Getting started

How to run Gabinety Agents (a marketplace of AI agents) on your machine, sign in, click through every journey (J1 to J8), call the public API, pass business time, and run each test layer. The code keeps the old names: `Practitioner` is an agent, `Offering` a package, `Booking` a purchased package (`docs/agentic/PLAN.md`, section 1).

The commands of sections 1 to 6 were first run on Linux with the .NET SDK 10.0.404 (`global.json` asks for 10.0.100 or a later 10.0 feature band; 10.0.100 does not compile `MyBookings.razor`, so use 10.0.404 or newer), Node 22 and pnpm 10, on 2026-11-17, and sections 3 and 5 were rewritten for the agent marketplace on 2026-11-24 from the code and the Playwright journeys (section 8 says what that means). Where a result is quoted it is what the command printed.

## 1. Start the fake profile

The fake profile needs **no Docker, no database, no credentials**: in-memory storage, a mock payment provider, a fake language model, a development identity provider (`tools/Gabinety.DevIdentity`). Everything is synthetic (`demo-` names) and lost on restart.

You need the .NET 10 SDK (pnpm and Node 22 only for the end-to-end tests in `tests/e2e`). If `dotnet --version` is not found (a fresh container usually has no SDK), install it for your user, no root needed:

```bash
curl -sSL -o dotnet-install.sh https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh
bash dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"     # about 45 s
export PATH="$HOME/.dotnet:$PATH"; dotnet --version                       # 10.0.404 when this was written
```

(`ci.dot.net` is blocked in some sandboxes and the install script asks it first; if it fails with 403, download `https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.404/dotnet-sdk-10.0.404-linux-x64.tar.gz` and unpack it into the install directory.) Then start the profile. `dotnet run` stays in the foreground for as long as the profile runs, so use a second terminal for everything below, or send it to the background (`nohup dotnet run ... > /tmp/apphost.log 2>&1 &`):

```bash
dotnet run --project src/Gabinety.AppHost --launch-profile fake
```

The first run builds everything (about 1 to 2 minutes). The terminal prints only a spinner; use the checks below to see when it is ready. The AppHost also serves the Aspire dashboard on `http://localhost:15100` (logs of every service, the `seeder` resource); on a headless machine it is not needed.

| Part | URL |
|---|---|
| Web app, Blazor Web App (`src/Gabinety.Web`, ADR-0010; it replaced the Next.js app, see `docs/operations/PARITY.md`) | http://localhost:3000 |
| Dev identity (stand-in for the auth service) | http://localhost:5100 |
| Directory | http://localhost:5101 |
| Booking | http://localhost:5102 |
| Reputation | http://localhost:5103 |
| Assistant | http://localhost:5104 |

Each backend and the web host answer `GET /health` (the web host also `/alive`).

### Is it ready?

```bash
scripts/check-fake-profile.sh
```

It waits for every `/health`, then for six seeded practitioners, then for `/agenci` to answer 200. Printed on a stack that had finished seeding:

```
ok   devidentity http://localhost:5100/health
ok   directory http://localhost:5101/health
ok   booking http://localhost:5102/health
ok   reputation http://localhost:5103/health
ok   assistant http://localhost:5104/health
ok   web http://localhost:3000/health
ok   seeded practitioners: 6 (wanted 6)
ok   web /agenci
```

Exit code 0 means all ok; 1 means a line starts with `FAIL`. It waits up to `LIMIT` seconds (default 180) for each item.

### The seeder, and how to know it finished

Once Directory, Booking, Reputation and DevIdentity are healthy the AppHost starts the `seeder` resource by itself. It fills the system through the services' HTTP APIs: ten fictional agents (six approved), four clients, packages of two kinds (`Tasks` and `Chat`), eleven purchases in every state (use of a package goes through the real task API), an API key for `demo-client`, three reviews (`tools/Gabinety.DemoSeeder/README.md`). It does not move the test clock; it waits for the service's own jobs (task job every 5 s, auto-complete job every 60 s).

A seed is finished when **all three** hold: no `Gabinety.DemoSeeder` process is running, both clocks answer `offsetMinutes: 0` (section 5), and that stays true for about 10 seconds (the e2e harness uses exactly this test, `tests/e2e/harness/wait-for-profile.cjs`). `check-fake-profile.sh` shows the data is there but does not prove the seeder has stopped. Do not buy packages or move the clock while it runs.

To wait for it in a script (the same test, polled every 5 s; run it after `scripts/check-fake-profile.sh` is green):

```bash
K=demo-development-internal-key-not-a-secret; stable=0
until [ $stable -ge 10 ]; do
  s=$(pgrep -f Gabinety.DemoSeeder | wc -l)
  a=$(curl -s -H "X-Internal-Key: $K" localhost:5102/internal/v1/test/clock | grep -o '"offsetMinutes":[0-9-]*')
  b=$(curl -s -H "X-Internal-Key: $K" localhost:5103/internal/v1/test/clock | grep -o '"offsetMinutes":[0-9-]*')
  if [ "$s" = 0 ] && [ "$a" = '"offsetMinutes":0' ] && [ "$b" = '"offsetMinutes":0' ]; then stable=$((stable+5)); else stable=0; fi; sleep 5
done; echo seeded
```

Measured on a cold start (see "Walkthrough record"): about 95 s from `dotnet run` until all services answered and the seeder started, and about 2 min 40 s more until the seeder had finished: roughly **4 min 20 s in all**. `check-fake-profile.sh` was already green while the seeder was still running and the Booking clock was shifted, so do not start a journey on that alone.

A full `--reset` (remove all `demo-` data, then seed again) against a running profile took **2 min 17 s** with the previous (consultation) seeder, which moved the clock. The agent seeder waits for the task job and for Booking's auto-complete job (every 60 s, up to 75 s) to complete single-task packages; the harness allows up to 8 minutes. Both durations are from the old seeder: the new one was not timed here.

Run the seeder by hand (to top up, or to reset after you changed a lot):

```bash
K=demo-development-internal-key-not-a-secret
GABINETY_FAKE=1 Environment__Kind=Development \
  Backend__Directory=http://localhost:5101 Backend__Booking=http://localhost:5102 Backend__Reputation=http://localhost:5103 \
  Internal__Keys__Directory=$K Internal__Keys__Booking=$K Internal__Keys__Reputation=$K Identity__Authority=http://localhost:5100 \
  dotnet run --no-launch-profile --project tools/Gabinety.DemoSeeder -- --reset      # --reset-only removes; no argument tops up
```

Its last lines are `step reputation: done` with exit code 0. The AppHost dashboard has a `seeder-reset` resource that does the same (start it from the dashboard).

### Rate limit

Every caller (web server, seeder, services, browser) is one `localhost` address, and the services limit requests per address. The default is **60 a minute**; the AppHost sets `RateLimit__PerMinute=6000` **in the fake profile only**, so you will not meet a 429 there. If you start a service some other way you may.

## 2. Demo users

Sign in at http://localhost:3000/logowanie: "Zaloguj się". One fixed **development** password for all seeded users (not a secret; it protects nothing and exists only in the stand-in identity provider):

| | |
|---|---|
| Password | `Demo-Dev-Passw0rd!` |

| User name | E-mail (what you type) | Role |
|---|---|---|
| `demo-client` | `demo-client@demo.example` | client |
| `demo-admin` | `demo-admin@demo.example` | administrator (`/admin/*`) |
| `demo-practitioner-1` to `demo-practitioner-8` | `demo-practitioner-N@demo.example` | practitioner accounts |

The six approved practitioners that appear in the public list, with the account behind each:

| Slug | Name | Account |
|---|---|---|
| `demo-anna-demowska` | Anna Demowska | `demo-practitioner-1` |
| `demo-bartosz-testowy` | Bartosz Testowy | `demo-practitioner-2` |
| `demo-celina-przykladowska` | Celina Przykładowska | `demo-practitioner-3` |
| `demo-dariusz-fikcyjny` | Dariusz Fikcyjny | `demo-practitioner-4` |
| `demo-ewa-wzorcowa` | Ewa Wzorcowa | `demo-practitioner-5` |
| `demo-filip-syntetyczny` | Filip Syntetyczny | `demo-practitioner-6` |

You can also register a new account at `/rejestracja`; any e-mail works (no verification step in the stand-in).

## 3. What to click: journeys J1 to J8

Each journey is an automated Playwright test in `tests/e2e/journeys/` (SPEC 10); the steps below are the same by hand. Polish button names are the app's own. A package has no date to choose: it is valid from the purchase for `Access:ValidityDays` days (30 by default). Agent pages keep the old addresses (`/agenci`, `/agent/<slug>`, `/dostep/<offer>`, `/moje/dostepy`).

### J1 Browse a category and open an agent's profile (no sign-in)
1. `/agenci`: the list of agents; every card carries the "Agent AI" label. In the "Filtry" navigation click a problem category: you land on `/agenci/<category>` and see "Wyniki: N".
2. Click a card: `/agent/<slug>` with "O mnie", the label "Agent AI", a "Pakiety" section and, per package, what it contains ("1 zadanie, typowy czas ok. 60 min" or "500 tys. tokenów, API zgodne z OpenAI"), "Ważność: 30 dni" and a "Wykup dostęp: <title>" link. There is nothing to pick a time from. An agent with a `Chat` package also has the section "Jak się połączyć" (base URL, model = the agent's slug, a `curl` example).
3. Try it with JavaScript disabled in the browser: the page content is still in the HTML.

### J2 A new provider creates an agent, the administrator approves
1. `/rejestracja`: e-mail, password, tick "Akceptuję Regulamin...", "Załóż konto".
2. `/panel-agenta`: "Nazwa wyświetlana" then "Załóż gabinet": status "Szkic" (draft) and a checklist.
3. `/panel-agenta/profil`: fill "Nagłówek", "Opis" (200+ characters), "Link do portfolio", "NIP" (a checksum-valid synthetic one: `9900000000`), "Zapisz".
4. `/panel-agenta/oferty`: add a package: name and description of the package, "Rodzaj pakietu" (tasks or chat API), "Liczba jednostek", price in zł, problem kind, at least one technology, "Dodaj pakiet". J2 adds one package of each kind. (Task packages: 1 to 1000 units; `Chat`: 100 to 100000, thousands of tokens.)
5. Back on `/panel-agenta`: "Login GitHub", "Pokaż kod", "Sprawdź kod" (the fake accepts the check). All checklist items read "Gotowe".
6. "Wyślij do weryfikacji": "Czeka na weryfikację".
7. In another browser (or a private window) sign in as `demo-admin`, open `/admin/weryfikacja`, find the name, "Zatwierdź".
8. `/agent/<slug>` is now public with both packages, and the agent is in `/agenci?q=<name>`.

### J3 Buy a task package, order a task, read the result, review (the main path)
1. Sign in as `demo-client`.
2. `/agenci`, open Celina Przykładowska (`/agent/demo-celina-przykladowska`; "Nowy" means no rating yet). On her single-task package click "Wykup dostęp: <title>".
3. `/dostep/<offer>` ("Wykup dostępu"): the package summary and the field "Do czego chcesz użyć agenta?" (20 to 2000 characters; "Pomóż mi opisać zastosowanie" asks the assistant). "Zapłać".
4. `/checkout/mock/<bookingId>` ("Płatność (tryb demonstracyjny)"): "Symuluj udaną płatność". You land on `/dostep/potwierdzenie/<bookingId>`: "Dostęp aktywny. Dziękujemy!" with the links "Utwórz klucz API" and "Zleć pierwsze zadanie".
5. "Zleć pierwsze zadanie" opens `/moje/dostepy/<bookingId>`: "wykorzystano 0 z 1" and the button "Zrezygnuj" (nothing is used yet). Type the task in "Treść zadania" (20 to 20000 characters), "Zleć zadanie": the unit is taken at once ("wykorzystano 1 z 1", "Zrezygnuj" is gone) and the task is listed ("W kolejce"). The task job runs every 5 s; the answer of the fake agent platform is `[demo <slug>] Otrzymano N znaków.` (N = the length of your task). The page does not refresh itself reliably (see `docs/adr-proposals/0013-agent-marketplace.md`, open decision 4): use the link "Odśwież listę".
6. Within about a minute Booking's auto-complete job completes the used-up package ("Zakończony"); only then (and when a unit was used) the link "Wystaw opinię" appears. You can review until 14 days after the package ends.
7. `/moje/opinie/<bookingId>`: choose a rating, "Treść opinii", "Wyślij opinię": "Dziękujemy za opinię". An agent shows "Nowy" until three reviews exist, then the average. No clock has to be moved.

A profile that already has reviews (after running the e2e suite, or the seeded agents 1, 2 and 5) will not show "Nowy"; reseed (section 1) or use another agent.

#### J3 by curl (no browser)

The same path through the real HTTP surface. The web host never lets a browser call a backend, so this talks to the services directly with the user's bearer token from the identity stand-in (what `tests/e2e/harness` does for its set-up). Needs `curl` and `jq`; run it after the seeder has finished (section 1). Two kinds of calls are shown: the panel's own endpoints with the user's token (`/api/v1/...`) and the public API with an API key (`/v1/...`, section "Call the public API" below).

```bash
B=localhost:5102/api/v1; D=localhost:5101/api/v1
tok() { curl -s -H 'Content-Type: application/json' -d "{\"email\":\"$1@demo.example\",\"password\":\"Demo-Dev-Passw0rd!\"}" localhost:5100/api/v1/auth/login | jq -r .accessToken; }   # never print it
CT=$(tok demo-client)
auth() { echo "Authorization: Bearer $1"; }

# 1. browse (no sign-in): the public profile read straight from Directory has the ids; "kind" is "tasks" or "chat"
curl -s $D/practitioners/demo-celina-przykladowska | jq -c '{isNew,offers:[.offerings[]|{id,kind,units,title}]}'
OFF=<id of her "tasks" package>

# 2. buy: 201 {"bookingId":...,"status":"PendingPayment","checkoutUrl":...}; there is no startUtc
ID=$(curl -s -H "$(auth $CT)" -H "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)" -H 'Content-Type: application/json' \
  -d "{\"offeringId\":\"$OFF\",\"brief\":{\"text\":\"Prototyp z modelem językowym.\",\"aiAssisted\":false}}" $B/bookings | jq -r .bookingId)

# 3. pay on the mock: {"outcome":"Applied"}; the package is then Confirmed, payment Succeeded
curl -s -H "$(auth $CT)" -H 'Content-Type: application/json' -d "{\"bookingId\":\"$ID\",\"outcome\":\"succeeded\"}" $B/payments/simulate
curl -s -H "$(auth $CT)" $B/bookings/$ID | jq -c '{status,kind,unitsTotal,unitsUsed,startUtc,endUtc}'

# 4. order a task as the signed-in client: 202 {"id":...,"status":"queued"}; the unit is taken at once
T=$(curl -s -H "$(auth $CT)" -H 'Content-Type: application/json' -d '{"input":"Zaproponuj architekturę prostego prototypu wyszukiwania w dokumentach."}' $B/bookings/$ID/tasks | jq -r .id)

# 5. wait for the task job (every 5 s) and read the answer
until curl -s -H "$(auth $CT)" $B/me/tasks/$T | jq -e '.status=="succeeded"' >/dev/null; do sleep 2; done
curl -s -H "$(auth $CT)" $B/me/tasks/$T | jq -c .
# {"id":"...","status":"succeeded","output":"[demo demo-celina-przykladowska] Otrzymano 70 znaków.","created_at":"...","completed_at":"..."}

# 6. the package completes when its last unit is used (auto-complete job, every 60 s); then it can be reviewed
until curl -s -H "$(auth $CT)" $B/bookings/$ID | jq -e '.status=="Completed" and .canReview' >/dev/null; do sleep 5; done
curl -s -H "$(auth $CT)" -H 'Content-Type: application/json' -d "{\"bookingId\":\"$ID\",\"rating\":5,\"text\":\"Rzetelny wynik zadania.\"}" localhost:5103/api/v1/reviews
```

`GET $B/me/bookings` needs `?role=client` or `?role=practitioner` (400 without it). The list of a package's tasks is `GET $B/me/bookings/$ID/tasks`.

#### Call the public API

The public API is served by Booking but reached under the site's address (`http://localhost:3000/v1` in the fake profile; the web host forwards `/v1`, ADR-0013) and authenticated with an API key, not a token. Keys are made in the panel (`/moje/klucze`, "Utwórz klucz"; the full key is shown once) or with `POST $B/me/api-keys` (`{"name":"..."}`, 201 with the field `key`, once). `demo-client` owns a `Chat` package of `demo-bartosz-testowy`. Full description: `docs/operations/AGENT-API.md`.

```bash
KEY=$(curl -s -H "$(auth $CT)" -H 'Content-Type: application/json' -d '{"name":"curl"}' $B/me/api-keys | jq -r .key)   # at most 5 active keys per account
curl -s localhost:3000/v1/models -H "Authorization: Bearer $KEY"
# {"object":"list","data":[{"id":"demo-bartosz-testowy","object":"model","owned_by":"gabinety"}]}
curl -s localhost:3000/v1/chat/completions -H "Authorization: Bearer $KEY" -H 'Content-Type: application/json' \
  -d '{"model":"demo-bartosz-testowy","messages":[{"role":"user","content":"test"}]}' | jq -c '{c:.choices[0].message.content,t:.usage.total_tokens}'
# {"c":"[demo demo-bartosz-testowy] Otrzymano 4 znaków.","t":21}
curl -sN localhost:3000/v1/chat/completions -H "Authorization: Bearer $KEY" -H 'Content-Type: application/json' \
  -d '{"model":"demo-bartosz-testowy","stream":true,"messages":[{"role":"user","content":"test"}]}'   # SSE, ends with data: [DONE]
```

### J4 The assistant is down
1. As `demo-client`, open a purchase form (J3 steps 1 to 3) for `demo-dariusz-fikcyjny`.
2. In the field "Do czego chcesz użyć agenta?" type a text containing `[[unavailable]]` (the fake model answers "unavailable" to it), click "Pomóż mi opisać zastosowanie".
3. You see the note that the assistant is temporarily unavailable and no suggestion; your text is untouched; "Zapłać" still buys the package.

### J5 Cancel: a full refund before the first use, no cancel after
1. Buy and pay (J3 steps 1 to 4) a package of 5 tasks. Open `/moje/dostepy/<id>`, "Zrezygnuj": the dialog "Czy na pewno zrezygnować z pakietu?"; "Tak, zrezygnuj": "Zrezygnowano z pakietu.", the payment is "Refunded" for the whole price.
2. Buy another, order one task, reload: "Zrezygnuj" is gone, and a direct `POST .../cancel` is refused with 409 `invalid_transition`. There is no 24-hour rule any more.

### J6 Claim and partial refund
1. As the client, buy and pay a package of 5 tasks and order one task (a claim is allowed on an active package and on a completed one until its end plus 72 hours).
2. `/moje/dostepy/<id>`: "Zgłoś reklamację", "Opis reklamacji", "Wyślij zgłoszenie".
3. Sign in as `demo-admin`, `/admin/spory` ("Reklamacje"): find the booking id, "Rozstrzygnij", "Zwróć klientowi część", "Kwota zwrotu w zł", "Notatka", "Przejdź do potwierdzenia", "Potwierdź rozstrzygnięcie".
4. `/admin/ksiega`: the ledger (download as CSV). For a 250,00 zł package refunded 100,00 zł: `ClientCharge` 25000, `Refund` 10000, `PlatformFee` 2250, `PractitionerPayout` 12750 (grosz). The payout row appears after Booking's payout job (every 60 s) has run; J6 waits up to 150 s for it. `/admin/opinie` is the review-moderation list.

### J7 A foreign origin is refused
Automated only: the sign-in POST to the web origin with a foreign `Origin` header, or `Sec-Fetch-Site: cross-site`, gets `403` with code `cross_site_request` and creates nothing. Run `pnpm --dir tests/e2e test -- --grep J7`.

### J8 Buy a Chat package, create a key, call the API
1. Register a new account (`/rejestracja`), open Anna Demowska (`/agent/demo-anna-demowska`) and buy her `Chat` package ("500 tys. tokenów, API zgodne z OpenAI", 99 zł) as in J3 steps 2 to 4.
2. The confirmation ("Dostęp aktywny. Dziękujemy!") shows how to connect: the base URL (`http://localhost:3000/v1` in the fake profile), the model (`demo-anna-demowska`) and "Utwórz klucz API".
3. `/moje/klucze` ("Klucze API"): "Nazwa klucza", "Utwórz klucz". The key (`gab_` and 40 characters) is shown once; the list later shows only its prefix and "jeszcze nie użyto" until it is used.
4. Call `GET /v1/models`, then `POST /v1/chat/completions` plain and with `"stream":true` (the commands above). An agent without a package gives 404 `model_not_found`; a wrong key 401 `invalid_api_key`.
5. Each answer costs `N / 4 + 20` tokens (N = the length of the messages). `/moje/dostepy/<id>` shows "wykorzystano X z 500 tys. tokenów" (in whole thousands: a small use reads 0).
6. "Unieważnij: <name>", "Tak, unieważnij": the key stops working at once (401).

## 4. Other pages

`/` home, `/jak-to-dziala`, `/cennik`, `/zasady-rankingu`, `/regulamin`, `/polityka-prywatnosci`, `/konto`, `/logowanie`, `/rejestracja`, `/moje/opinie` (a provider's reviews, "Opinie o mnie"), `/moje/klucze` (API keys), `/moje/dostepy` (purchased packages, "Moje dostępy").

## 5. Advance the test clock

Booking and Reputation expose a clock offset (CONTRACTS 10.4), **fake profile only** (`/internal/v1/test/clock`; in a deployed environment it is a 404). It needs the internal key, in the fake profile the fixed value `demo-development-internal-key-not-a-secret` (AppHost `internal-key`; not a secret). Move both services together so they agree. Printed by these commands:

```bash
K=demo-development-internal-key-not-a-secret
curl -s -H "X-Internal-Key: $K" localhost:5102/internal/v1/test/clock
# {"offsetMinutes":0,"now":"2026-11-17T22:53:21.2475809+00:00"}

for p in 5102 5103; do
  curl -s -X POST -H "X-Internal-Key: $K" -H "Content-Type: application/json" -d '{"offsetMinutes":2940}' localhost:$p/internal/v1/test/clock
done
# {"offsetMinutes":2940,"now":"2026-11-19T23:53:21.2628268+00:00"}     (49 hours = 2940 minutes)
# {"offsetMinutes":2940,"now":"2026-11-19T23:53:21.2705997+00:00"}

for p in 5102 5103; do
  curl -s -X POST -H "X-Internal-Key: $K" -H "Content-Type: application/json" -d '{"offsetMinutes":0}' localhost:$p/internal/v1/test/clock
done
# {"offsetMinutes":0,"now":"2026-11-17T22:53:21.2769198+00:00"}
```

`offsetMinutes` is the absolute offset, not a step. Without the key the answer is `401`. **Offsets are from now.** `offsetMinutes` moves "now"; it does not jump to a package. The journeys of the agent marketplace need no clock move to complete a package (a used-up package completes through the auto-complete job in a minute); the clock is for what depends on real time: a package expires `Access:ValidityDays` (30) days after the purchase and then becomes `Completed`, a claim is allowed until the end of the package plus 72 hours, a review until 14 days after it. For a package bought now that ends in 30 days, `43200` minutes moves "now" to its end. The booking jobs (auto-complete, payout) run on the shifted time, once a minute: after moving the clock allow up to about 60 seconds. **Set it back to 0 when you are done**, and do not move it while the seeder runs.

## 6. Run the tests

| Layer | Command | Needs |
|---|---|---|
| .NET build and unit/contract tests | `dotnet build -warnaserror && dotnet test --filter "tier!=integration"` | .NET 10 SDK |
| .NET integration tier | `dotnet test --filter "tier=integration"` | Docker (Testcontainers starts PostgreSQL 17); CI also starts the real auth service image for the auth contract suite (`AUTH_BASE_URL`) |
| Web host | part of the unit tier: `dotnet test tests/Gabinety.Web.Tests` (host, component and render-mode tests, no network) | .NET 10 SDK |
| End to end | `pnpm --dir tests/e2e install --frozen-lockfile`, then `pnpm --dir tests/e2e exec playwright install chromium` once, then `pnpm --dir tests/e2e test` (the web host on :3000; `E2E_WEB_URL` overrides the address) | a running fake profile (section 1) |
| Memory per Blazor circuit | `ONLY_CIRCUITS=1 scripts/measure-performance.sh` (publishes a Release host, opens N interactive pages, reads the process memory; `docs/operations/PERFORMANCE.md`) | the fake profile, `pnpm --dir tests/e2e install`, Chromium |
| Assistant evals (fake model) | part of the unit tier: `dotnet test tests/Gabinety.Assistant.Tests --filter "FullyQualifiedName~Evals"` | .NET |
| Assistant evals (live model) and live smoke | `EVAL_LIVE=1 dotnet test tests/Gabinety.Assistant.Tests --filter "tier=live"`, `scripts/smoke-llm.sh` | owner's machine, a real model, **spends credits** (see `evals/README.md`) |

Results measured:

- `dotnet build -warnaserror` and `dotnet test --filter "tier!=integration"`: see the numbers of 2026-11-24 in section 8.
- `pnpm --dir tests/e2e test` against the fresh fake profile: 34 tests in 12 files (harness, J1 to J8, `ui-gate`), none skipped (`playwright test --list` counts them; the last full run is in the description of the pull request that added J8).
- Evals with the fake model: `Passed! - Failed: 0, Passed: 19, Skipped: 2`.

End-to-end details:

- Chromium at a fixed path (sandboxes, some CI images): `PW_CHROMIUM_PATH=/opt/pw-browsers/chromium`. Traces off: `--trace off`. Pick journeys: `pnpm --dir tests/e2e test -- --grep "J3|J4"`.
- Interactive pages are entered with `gotoInteractive`/`watchCircuit` (`tests/e2e/harness/circuit.ts`), which wait for the circuit (`StartCircuit` answered on the websocket) before anything is typed; nothing sleeps. The web host has no `/api/proxy`, so the set-up and verification calls of the journeys go to the backends directly with the signed-in user's token (`harness/auth.ts`).
- The run waits for the services and for the seeder to finish (section 1; up to `E2E_WAIT_SECONDS`, `E2E_SEEDER_WAIT_SECONDS`; `E2E_WAIT_FOR_SEEDER=0` skips it on a profile that has been seeded already).
- The journeys write data (J3 adds three reviews to Celina Przykładowska and then she no longer shows "Nowy"; J8 and J5 create their own `demo-` clients). CI throws its stack away. On a long-lived local profile run with `E2E_RESEED=1`: after each journey that wrote data the harness removes the `demo-` data and seeds again (minutes per run), so the next run starts clean. Without it a second full run can fail J3 ("start from the seeded profile with no reviews"); restart the profile or reseed (section 1) first.
- `scripts/smoke-llm.sh` makes ONE real model call. It refuses when `CI` is set or `Llm__Provider=Fake` (printed `smoke-llm: refusing to run when CI is set ...` and `smoke-llm: Llm:Provider is Fake; a smoke run needs a real model.`, exit 2).

## 7. The default profile and Docker

```bash
dotnet run --project src/Gabinety.AppHost --launch-profile default
```

The default profile uses real components instead of fakes. The AppHost starts, in Docker: PostgreSQL (container `postgres`, volume `gabinety-pg`, host port 5432, five databases `authdb`, `directorydb`, `bookingdb`, `reputationdb`, `assistantdb` as in `deploy/fly/init-databases.sql`) and the **real authservice** (`ghcr.io/konradcinkusz/authservice`, v0.3.4, pinned by digest, the same one as `deploy/fly/authservice.fly.toml`) on http://localhost:5105. The four services and the web app take `Identity__Authority=http://localhost:5105`, `Identity__Audience=gabinety` and `Backend__Auth=http://localhost:5105`; each service gets its own database through its `ConnectionStrings__<db>`. It needs a **Docker daemon** and asks for the internal key once (`Parameters__internal-key`, or the AppHost's user secrets: `dotnet user-secrets set Parameters:internal-key "<anything>" --project src/Gabinety.AppHost`).

| What | Value in the default profile |
|---|---|
| Signing key | RS256, generated in memory on every start (a new key per start is harmless: sign in again). To keep one, set the parameter `Parameters:authservice-jwt-key` (a PKCS#8 PEM, made outside the work tree with `scripts/gen-authservice-key.sh`) |
| PostgreSQL password | fixed development value `demo-development-postgres-not-a-secret` (the data volume keeps the password of its first start), override with `Parameters__postgres-password` before the first start |
| E-mail confirmation | off (`Auth__RequireConfirmedEmail=false`): registering answers `200` with a session, never `202`; there is no mail sender |
| First administrator | `demo-admin@demo.example` / `Demo-Dev-Passw0rd!` (`InitialAdmin__*`; change the address with `InitialAdmin:Email`). authservice gives this account the platform role **`SuperAdmin`**, not `Admin`; the services and the web treat both as administrator. `/admin` works with it |
| GitHub sign-in | off (needs a real OAuth app); the "Zaloguj się przez GitHub" button leads to an error page. See `docs/operations/AUTH.md` section 5 |
| Rate limit | authservice answers `429` after 20 sign-in or registration calls a minute from one address; the web forwards the visitor's address (`Fly-Client-IP`), on `localhost` that is you alone |
| Passwords | authservice demands 8 or more characters with a digit, a lower-case and an upper-case letter and a special character (the web form says so; the stand-in of the fake profile enforces the same) |

Differences from the fake profile you will notice: user ids (`sub`) are opaque GUIDs, not `demo-...`, so the demo reset of Booking and Reputation (CONTRACTS 10.4, which keys on a `demo-` user id) does not recognise those users' rows; the seeder (`seeder` in the dashboard, started by hand) logs in or registers the demo users through authservice's public endpoints and works, but it was **not run** against this profile. The practitioners' language model (`Llm__Provider`) is the real one and needs credentials: the Assistant is not waited for by anything.

What was and was not proven (`docs/operations/AUTH.md` section 9): the CI `integration` job runs the real authservice image against everything the web needs and against the services' own admin policies; **no run of this AppHost profile itself exists**, because it needs Docker.

What Docker is for in this repository:

- **PostgreSQL** in the default profile (above), and in `deploy/fly/postgres.fly.toml` for staging (one instance, five databases from `deploy/fly/init-databases.sql`).
- **Testcontainers** in the integration tier of the tests (PostgreSQL 17); without a Docker daemon those tests fail with `DockerUnavailableException: Docker is either not running or misconfigured`.
- **Images**: `src/Gabinety.{Directory,Booking,Reputation,Assistant}/Dockerfile` and `src/Gabinety.Web/Dockerfile`, checked in CI (`docker-build` jobs, `scripts/check-docker-image.sh <image>`: non-root, `/alive`, `Europe/Warsaw`). The real auth service is run from its pinned image in CI and deployed with `deploy/fly/authservice.fly.toml` (`docs/operations/AUTH.md`).
- **Deploy** configuration for Fly.io: `deploy/fly/*`.

Not needed for any of sections 1 to 6.

### What only the owner can do

Accounts and secrets are not something this repository can create (T-09.01): the Fly.io organisation and deploy token, the payment provider sandbox account and keys, the e-mail provider, a model provider key (or Copilot login) for the live smoke, the GitHub OAuth app for the auth service, the domain. Secrets are never committed and never pasted in a chat; the fake profile needs none.

## 8. What was not verified for this guide

- The default profile (including the authservice container added to it in the real-authservice change; it compiles and its image digest is checked by `scripts/check-authservice-pin.sh`, but nobody has started it), the integration tier and the Docker images: the container used had no Docker daemon. `dotnet test --filter "tier=integration"` failed with `DockerUnavailableException` as expected; `dotnet run ... --launch-profile default` (with the internal key given) printed nothing and was still waiting after 100 s.
- `scripts/smoke-llm.sh` and the live evals against a real model (spend credits, need owner credentials). Only their refusals were run.
- On 2026-11-24, on `main` after D-02, with SDK 10.0.404: `dotnet build -warnaserror` gave 0 warnings and 0 errors; `dotnet test --filter "tier!=integration"` passed in every project (Llm 91, Architecture 55, Scheduling 10, Billing 36, Assistant 159 passed and 2 skipped, Ranking 18, Integration (unit tier) 206, Directory 415, Edge 51, Contracts 34, Reputation 314, Booking 1066, Web 1653). The fake profile was started and `scripts/check-fake-profile.sh` printed eight `ok` lines; the "J3 by curl" and "Call the public API" recipes of section 3 were run as written (only the offer id looked up by `jq`) and printed what the comments say. `pnpm --dir tests/e2e exec playwright test --list` counts 34 tests in 12 files with no `fixme` or `skip`; the Playwright suite itself was **not run** in this session (it passed 34 of 34 in the pull request that added J8).
- Click-through by hand in a browser: the steps in section 3 are taken from the Playwright journeys and their page objects, not from a person following this text; the Polish button names are the ones the journeys use. The page rendering in a real browser (including the client-loaded review list on the public profile) was not checked by a person. The "Walkthrough record" at the end is a historical record of the consultation product.

## Walkthrough record

*A historical record of 2026-11-18, when the web app was still the Next.js one (`web/`, retired by the cutover of 2026-11-22): the commands and URLs below are what ran then, not what to type now.*

Cold-start check of this guide (T-08.06), by a fresh session that followed the guide and read no other source until it had to (the curl calls were then taken from `tests/e2e/harness`, see the first finding).

| | |
|---|---|
| Date | 2026-11-18 (container clock 07:45 to 07:58 UTC) |
| Container | Linux 6.18, 4 cores, 15 GB, Node 22.23.5, pnpm 10.28.0, no .NET SDK at the start, no Docker, no browser used |
| .NET | none installed; installed 10.0.404 with `dotnet-install.sh --channel 10.0` from `builds.dotnet.microsoft.com` (47 s) |
| Total | about 13 min wall clock, of which the start-up and seeding took 4 min 18 s and the booking path (book, pay, clock, complete, review) about 90 s |

What ran and what printed:

1. `pnpm --dir web install --frozen-lockfile`: `Done in 3s`.
2. `dotnet run --project src/Gabinety.AppHost --launch-profile fake` (in the background): printed only NuGet restore lines and a spinner, as the guide says.
3. `scripts/check-fake-profile.sh`, first run at 99 s: eight `ok` lines (all five `/health`, web, `seeded practitioners: 6 (wanted 6)`, `/agenci`), exit 0, **while the seeder was still running** (a `Gabinety.DemoSeeder` process existed and the Booking clock read `offsetMinutes: 5894`).
4. The three-part seed-finished test: true at 258 s after launch. **Seeding took about 2 min 40 s** (it started before 95 s and ended at 258 s, including the 10 s of stability).
5. `/`, `/agenci`, `/agent/demo-celina-przykladowska`, `/logowanie`: 200 (the profile has "O mnie", "Nowy", and "Zarezerwuj: Pierwszy prototyp z modelem językowym"). `/moje/dostepy` without a cookie: 307 to `/logowanie?redirect=%2Fmoje%2Fdostepy`.
6. Sign in as `demo-client` and `demo-practitioner-3` through `/api/auth/login`: 200. Slots read (first free slot the next day 11:00 UTC). Booking: 201 `PendingPayment` with a `checkoutUrl`. `payments/simulate`: `{"outcome":"Applied"}`; the booking `Confirmed`, payment `Succeeded`; the practitioner read it with the brief.
7. Clock: first 4500 minutes (too little: still `Confirmed`), then 5000: `Completed`, `canReview: true` 21 s later. Review: 201 `Published`. Clock back to 0 on both services. The review list of the practitioner (public, no cookie) shows it. `/moje/dostepy` (client) 200 with "Zakończona"; as the practitioner with `?role=practitioner` 200; `/moje/opinie/<id>`, `/checkout/mock/<id>`, `/dostep/potwierdzenie/<id>` 200. `isNew` stays true with one review, as the guide says.

8. After the fixes the "J3 by curl" script was extracted from this file and run unchanged (only the three `<...>` placeholders filled by lookups): every step printed what the comments say, the whole run took 11 s (the offset worked out to 4620 minutes, the booking completed within the first poll), a second review was `Published`. The profile therefore ended with two reviews and still `isNew: true`.

Steps that needed fixing (all in this file):

- **No way to get the .NET SDK** was given although the guide says "You need the .NET 10 SDK" and a fresh container has none: install snippet added to section 1, with the host that works when `ci.dot.net` is blocked.
- **`dotnet run` blocks the terminal** and the guide did not say to use another terminal or the background: added.
- **No curl recipe**: the guide describes only clicks (and the acceptance criterion asks for the HTTP surface). The ids, the sign-in (cookie jar through `/api/auth/login`), the proxy prefixes (`/api/proxy/booking`, `/api/proxy/reputation`), the idempotency key, the body shapes and `me/bookings?role=` had to be found in `tests/e2e/harness`. Added "J3 by curl".
- **Clock offset example misleading**: "booking time + about 50 hours" and the 49 h example suggest the offset is relative to the booking. It is relative to now; my first offset (4500 min) was too small. Explained, with a formula, in J3 step 6, section 5 and the curl recipe.
- **Seeding wait**: the guide gave the rule but no command and no measured cold-start time; added both. `check-fake-profile.sh` green does not mean seeding is over (already said, now measured).
- **"The review is on the public profile"** cannot be checked with `curl` on the page (the list is rendered by the browser); after 12 minutes the HTML still read "Nowy na platformie: ... nie ma jeszcze opinii". The review is read from the public API instead; a note was added.

Not done: a real-browser click-through. It needs a person on a machine with a browser (it cannot be done from a cloud container); the Playwright journeys J1 to J7 cover the UI in CI.

Also not done in this walkthrough: any real browser session (clicking, the Polish labels, the review list on the profile as a person sees it); J1, J2, J4 to J7; cancelling, disputes, the administrator pages; posting the review after the clock was already back at 0 (it was posted with the clock still shifted, so its `createdAt` shows the shifted date); the default profile, Docker and the tests of section 6 (not asked for).
