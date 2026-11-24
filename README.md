<p align="center">
  <img src="docs/assets/logo.svg" alt="Gabinety Tech" width="360">
</p>

<p align="center">
  <a href="https://github.com/konradcinkusz/gabinety-tech/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/konradcinkusz/gabinety-tech/actions/workflows/ci.yml/badge.svg"></a>
  <a href="https://github.com/konradcinkusz/gabinety-tech/actions/workflows/pages.yml"><img alt="Pages" src="https://github.com/konradcinkusz/gabinety-tech/actions/workflows/pages.yml/badge.svg"></a>
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512bd4">
  <img alt="Blazor" src="https://img.shields.io/badge/web-Blazor-512bd4">
  <img alt="PostgreSQL" src="https://img.shields.io/badge/db-PostgreSQL-336791">
  <img alt="Aspire" src="https://img.shields.io/badge/orchestration-Aspire-512bd4">
</p>

**Gabinety Agents** to polski marketplace agentów AI w stałej cenie. Klient znajduje agenta w katalogu, wykupuje **pakiet dostępu** i płaci. Potem korzysta z agenta na jeden z dwóch sposobów:

- pakiet `Tasks`: zleca zadania w panelu albo przez `POST /v1/tasks`; zadanie wykonuje się w tle, a wynik klient czyta w panelu lub przez `GET /v1/tasks/{id}`;
- pakiet `Chat`: pula tokenów na API zgodne z OpenAI (`/v1/chat/completions`, np. GitHub Copilot BYOK).

Agentów nie uruchamia ta aplikacja. Woła zewnętrzną platformę agentów przez jeden interfejs (`IAgentUpstream`); w profilu `fake` odpowiada deterministyczny zamiennik. Repozytorium powstało z Gabinety Tech (konsultacje z programistami); przebudowa była rozpisana w [docs/agentic/PLAN.md](docs/agentic/PLAN.md). W kodzie zostały dawne nazwy (`Practitioner` to agent, `Offering` to pakiet, `Booking` to zakupiony pakiet).

## Co działa

Cały system uruchamia się lokalnie w profilu `fake`, bez Dockera i bez kluczy:

- katalog agentów i profile z plakietką „Agent AI”, rejestracja i logowanie, panel dostawcy (agent, pakiety, weryfikacja przez administratora),
- zakup pakietu bez wyboru terminu, płatność próbna, ważność pakietu (domyślnie 30 dni), rezygnacja z pełnym zwrotem przed pierwszym użyciem, reklamacje i opinie, panel administratora,
- „Moje dostępy” (zużycie pakietu, zlecanie zadań, wyniki) oraz klucze API w `/moje/klucze` (do 5 aktywnych na konto, pełny klucz widać tylko raz),
- publiczne API `/v1`: obsługuje je usługa Booking, a strona (`Gabinety.Web`) przekazuje je pod swoim adresem (klient ma jeden adres): `GET /v1/models`, `POST /v1/chat/completions` (zwykłe i strumień SSE), `POST /v1/tasks`, `GET /v1/tasks/{id}`; błędy w kształcie OpenAI, limit żądań per klucz, rozliczanie tokenów i jednostek ([docs/operations/AGENT-API.md](docs/operations/AGENT-API.md)),
- klient `HttpAgentUpstream` do platformy zgodnej z OpenAI (`Agents:Provider=Http`; sprawdzony testami, nie z prawdziwą platformą),
- dane demonstracyjne (sześciu zatwierdzonych agentów, pakiety `Tasks` i `Chat`, zakupy w każdym stanie) i zamiennik dostawcy tożsamości do prac lokalnych,
- asystent oparty o model językowy, który pomaga opisać zastosowanie agenta,
- osiem ścieżek end-to-end (J1 do J8): 34 testy Playwrighta, bez `fixme` i pominięć (`tests/e2e/README.md`).

Prawdziwy [authservice](https://github.com/konradcinkusz/authservice) jest dołączony do profilu domyślnego (potrzebuje Dockera) i sprawdzany w CI na wszystkich przepływach, których potrzebuje web (`docs/operations/AUTH.md`); samego profilu domyślnego nikt jeszcze nie uruchomił.

**Jeszcze nie:**

- prawdziwe płatności: kod Stripe Connect z wypłatami dla dostawców zostaje, ale w modelu agentowym sprzedawcą ma być platforma; zmiany przed płatnościami: [docs/operations/PAYMENTS-AGENTS.md](docs/operations/PAYMENTS-AGENTS.md),
- połączenie z prawdziwą platformą agentów (w CI i lokalnie działa tylko zamiennik),
- podłączenie przez GitHub Copilot: kroki po stronie Copilota są niezweryfikowane ([docs/operations/COPILOT-BYOK.md](docs/operations/COPILOT-BYOK.md)),
- lista decyzji właściciela (adres `/v1` rozstrzygnięty: pod adresem strony): [docs/adr-proposals/0013-agent-marketplace.md](docs/adr-proposals/0013-agent-marketplace.md),
- weryfikacja prawna regulaminu i polityki prywatności (znacznik `DO WERYFIKACJI PRAWNEJ`), logowanie przez GitHub do końca (potrzebuje prawdziwej aplikacji OAuth), potwierdzanie e-mail, prawdziwy e-mail i model językowy (wymagają kont i sekretów właściciela) oraz zweryfikowane wdrożenie,
- reszta etapu E planu: E-03 do E-06 (płatności rzeczywiste, wielu agentów jednego dostawcy, osobna usługa `/v1`, zmiana nazw w kodzie) czekają na decyzje właściciela; napisy „gabinet” w tekstach strony i nazwy w kodzie (`Practitioner`, `Booking`) zostają dawne. E-01 (martwy kod terminów) i E-02 (adresy `/agenci`, `/agent/{slug}`, `/dostep/...`, `/moje/dostepy`, `/panel-agenta`; stare adresy odpowiadają 301) są zrobione.

## Szybki start

Potrzebne: .NET 10 SDK (pnpm i Node tylko do testów end-to-end w `tests/e2e`).

```bash
dotnet run --project src/Gabinety.AppHost --launch-profile fake
scripts/check-fake-profile.sh   # sprawdza wszystkie usługi
```

Frontend to **Blazor Web App** (`src/Gabinety.Web`, ADR-0010) na <http://localhost:3000>; Next.js (`web/`) został wycofany (przełączenie: [docs/operations/PARITY.md](docs/operations/PARITY.md), [docs/operations/CUTOVER.md](docs/operations/CUTOVER.md)). Ścieżki użytkownika J1-J8 przechodzą na nim: `pnpm --dir tests/e2e test`. Pełny przewodnik (logowanie, ścieżki, zegar testowy, warstwy testów): [docs/GETTING-STARTED.md](docs/GETTING-STARTED.md).

### Przykład: czat z agentem przez curl

W profilu `fake` użytkownik `demo-client` ma dane dostępy, w tym pakiet `Chat` agenta `demo-bartosz-testowy`. Zaloguj się jako `demo-client` (hasło deweloperskie w [docs/GETTING-STARTED.md](docs/GETTING-STARTED.md)), utwórz klucz w <http://localhost:3000/moje/klucze> (pełny klucz widać tylko raz) i wywołaj `/v1` pod adresem strony (strona przekazuje je do Bookinga):

```bash
KEY=gab_...   # klucz z /moje/klucze
curl -s http://localhost:3000/v1/models -H "Authorization: Bearer $KEY"
curl -s http://localhost:3000/v1/chat/completions \
  -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
  -d '{"model":"demo-bartosz-testowy","messages":[{"role":"user","content":"test"}]}'
# {"id":"chatcmpl-demo","object":"chat.completion",...,"choices":[{"message":{"content":"[demo demo-bartosz-testowy] Otrzymano 4 znaków."}}],"usage":{"total_tokens":21,...}}
```

Dodanie `"stream":true` daje strumień SSE zakończony `data: [DONE]`. Opis całego API: [docs/operations/AGENT-API.md](docs/operations/AGENT-API.md).

## Architektura

| Element | Rola | Port (fake) |
|---|---|---|
| `Gabinety.Directory` | katalog agentów, profile, pakiety, weryfikacja | 5101 |
| `Gabinety.Booking` | zakupione pakiety, płatności, reklamacje, klucze API, publiczne API `/v1`, zadania agentów | 5102 |
| `Gabinety.Reputation` | opinie i oceny | 5103 |
| `Gabinety.Assistant` | asystent opisu zastosowania (model językowy) | 5104 |
| `Gabinety.DevIdentity` | zamiennik dostawcy tożsamości | 5100 |
| `src/Gabinety.Web` | frontend: Blazor Web App (ADR-0010: `docs/adr-proposals/0010-blazor-web-app-frontend.md`), renderowanie statyczne, interaktywne tylko tam, gdzie trzeba | 3000 |

Usługi .NET 10 (minimal API, EF Core, PostgreSQL) uruchamia Aspire AppHost. Autoryzacja panelu: tokeny RS256 z [authservice](https://github.com/konradcinkusz/authservice); autoryzacja `/v1`: klucz API `gab_...`. Platforma agentów to osobna aplikacja za interfejsem `IAgentUpstream` (`Agents:Provider`: `Fake` lub `Http`). Pieniądze to `long` w groszach, czas pochodzi z `TimeProvider`.

## Weryfikacja

```bash
dotnet build -warnaserror && dotnet test --filter "tier!=integration"
```

Testy integracyjne (Docker) i E2E (Playwright) działają w CI.

## Dokumentacja

- Specyfikacja, kontrakty i decyzje: [gabinety-tech-spec](https://github.com/konradcinkusz/gabinety-tech-spec)
- Wdrożenie i operacje: [docs/operations](docs/operations); API agentów: [AGENT-API.md](docs/operations/AGENT-API.md); GitHub Copilot BYOK: [COPILOT-BYOK.md](docs/operations/COPILOT-BYOK.md)
- Plan przebudowy i decyzje: [docs/agentic/PLAN.md](docs/agentic/PLAN.md), [docs/adr-proposals/0013-agent-marketplace.md](docs/adr-proposals/0013-agent-marketplace.md)
- Zasady pracy w repozytorium: [AGENTS.md](AGENTS.md)
