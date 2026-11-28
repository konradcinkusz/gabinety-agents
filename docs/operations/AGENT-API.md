# API agentów (`/v1`)

Publiczne API usługi Booking. Opisuje to, co jest w kodzie (`src/Gabinety.Booking/Features/AgentApi`, `Agents`, `Jobs/AgentTasks`), nie to, co mówił plan (`docs/agentic/PLAN.md`, sekcje 2.3 i 2.4); tam, gdzie kod różni się od planu, jest to zaznaczone. Nazwy w kodzie zostały dawne: agent to `Agent`, pakiet to `Offering`, zakupiony pakiet to `Booking`.

Pole `model` w API to **slug agenta** (np. `demo-bartosz-testowy`). Jedno konto dostawcy może prowadzić wielu agentów (E-04); każdy ma własny slug, pakiety i zatwierdzenie, a klient widzi ich jak każdych innych agentów: klucz i `/v1` nie zmieniły się.

## Uwierzytelnianie

`Authorization: Bearer gab_...`. Klucz ma format `gab_` i 40 znaków base62 (`RandomNumberGenerator`). Klient tworzy klucze w panelu (`/moje/klucze`) albo przez `POST /api/v1/me/api-keys` z tokiem użytkownika (ciało `{"name":"..."}`, nazwa do 60 znaków; odpowiedź 201 z polem `key` tylko ten jeden raz). Na koncie może być do 5 aktywnych kluczy; szósty daje 422 `validation_failed`. `DELETE /api/v1/me/api-keys/{id}` unieważnia klucz (204; cudzy klucz to 404).

W bazie jest tylko skrót SHA-256 (małe litery hex) i 8-znakowy prefiks po `gab_`. Klucz nie trafia do logów. `LastUsedAt` zmienia się najwyżej raz na minutę. Jeden klucz działa dla wszystkich agentów, do których konto ma aktywny pakiet.

Brak nagłówka, zły format, nieznany lub unieważniony klucz: 401 `invalid_api_key` z nagłówkiem `WWW-Authenticate: Bearer`.

## Błędy w kształcie OpenAI

Każdy błąd `/v1` ma kształt `{"error":{"message":"...","type":"...","code":"..."}}` (nie `problem+json`). Komunikaty to stałe teksty; nie ma w nich niczego, co napisał użytkownik, ani treści odpowiedzi dostawcy.

| Status | `code` | `type` | Kiedy |
|---|---|---|---|
| 400 | `invalid_request_error` | `invalid_request_error` | złe ciało (nie obiekt JSON), brak `model`, złe pole zadania |
| 401 | `invalid_api_key` | `invalid_request_error` | patrz wyżej |
| 404 | `model_not_found` | `invalid_request_error` | agent nie istnieje albo konto nie ma aktywnego pakietu właściwego rodzaju (też po wygaśnięciu pakietu) |
| 404 | `task_not_found` | `invalid_request_error` | `GET /v1/tasks/{id}`: brak zadania albo cudze zadanie |
| 413 | `request_too_large` | `invalid_request_error` | ciało większe niż 2 MB |
| 429 | `insufficient_quota` | `insufficient_quota` | konto ma niewygasły pakiet tego agenta, ale jest wyczerpany |
| 429 | `rate_limit_exceeded` | `rate_limit_error` | limit żądań klucza (niżej); odpowiedź ma `Retry-After` w sekundach |
| 502 | `upstream_error` | `api_error` | dostawca agentów nie odpowiedział albo odpowiedział 5xx, zanim wysłano pierwszy bajt |

Odpowiedź 4xx dostawcy agentów na zwykłe `chat/completions` jest przekazywana bez zmian (ze swoim statusem i ciałem); 5xx dostawcy zamienia się na 502 `upstream_error`.

## Limit żądań

Limit `/v1` jest **per klucz** (nie per konto ani adres). To grupa „pozostałe” z `RateLimitTable`: `RateLimit:PerMinute` żądań na minutę (domyślnie 60; profil `fake` ustawia 6000). Dotyczy też `GET`. Przekroczenie: 429 `rate_limit_exceeded`. Limit jest w pamięci jednej instancji. Limity zakupu (10 na minutę i 30 na dobę), opinii i pozostałe punkty `/api/v1` są osobne i liczone per użytkownik.

## Wybór pakietu i rozliczanie

Pakiet nadaje się do użycia, gdy ma stan `Confirmed`, `EndUtc > teraz` i `UnitsUsed < UnitsTotal`. Przy kilku pakietach tego samego agenta i rodzaju używany jest **najstarszy** (po `CreatedAt`). Pakiet jest ważny `Access:ValidityDays` dni od zakupu.

| Rodzaj | Jednostka | Kiedy zużycie | Wyczerpanie |
|---|---|---|---|
| `Tasks` | 1 zadanie | od razu przy `POST /v1/tasks`, warunkowo (nigdy ponad `UnitsTotal`; dwa równoległe żądania o ostatnią jednostkę: jedno dostaje 202, drugie 429) | `insufficient_quota` |
| `Chat` | 1 token (oferta trzyma tysiące, zakup mnoży przez 1000) | po fakcie, z `usage.total_tokens` odpowiedzi dostawcy (suma tokenów wejścia i wyjścia, jedna stawka) | `insufficient_quota` |

Rozliczenie czatu po fakcie oznacza, że **ostatnie żądanie może przekroczyć pulę**: `UnitsUsed` bywa większe niż `UnitsTotal`, a panel pokazuje pozostałe jako `max(0, razem - użyte)`. Dokładnie:

- zwykłe wywołanie: tokeny naliczane tylko przy odpowiedzi 2xx, w której dostawca podał `usage.total_tokens` większe od zera (bez `usage` nic nie jest naliczane);
- strumień: tokeny z ostatniego fragmentu `usage`, naliczane po zakończeniu strumienia;
- **rozłączony strumień nie jest dziś rozliczany**: `IAgentUpstream.StreamAsync` zwraca liczbę tokenów dopiero po całym strumieniu, więc po rozłączeniu klienta nic nie jest naliczane, choć dostawca mógł wykonać pracę (do decyzji właściciela: `docs/adr-proposals/0013-agent-marketplace.md`);
- błąd dostawcy przed pierwszym bajtem (502): nic nie jest naliczane.

Każde zużycie zapisuje `UsageRecord` (tabela `usage_records`: pakiet, klucz, rodzaj, liczba jednostek, czas; same liczby). Zwrot jednostki za nieudane zadanie to rekord z `Units = -1`. W logach jest tylko: identyfikator pakietu, slug agenta (z pakietu, nie z żądania), liczba tokenów, status, czas trwania.

## Punkty końcowe

### `GET /v1/models`

Agenci, u których konto ma użyteczny pakiet `Chat` (posortowani po slugu):

```json
{"object":"list","data":[{"id":"demo-bartosz-testowy","object":"model","owned_by":"gabinety"}]}
```

Pakiety `Tasks` się tu nie pokazują.

### `POST /v1/chat/completions`

Zgodne z OpenAI. Ciało (do 2 MB) idzie do dostawcy **bez zmian** (`tools`, `tool_choice`, `temperature` i reszta), tylko `model` jest podmieniany na slug z pakietu. `stream: true` daje strumień; inaczej zwykła odpowiedź.

```bash
curl -s http://localhost:5102/v1/chat/completions \
  -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
  -d '{"model":"demo-bartosz-testowy","messages":[{"role":"user","content":"test"}]}'
```

- Zwykła odpowiedź: JSON dostawcy i jego status, bez zmian.
- Strumień: `Content-Type: text/event-stream`, `Cache-Control: no-cache`, wyłączone buforowanie; linie SSE dostawcy są przepisywane bez zmian, kończy je zwykle `data: [DONE]`. Serwer wymusza `stream_options.include_usage = true`, więc ostatni fragment niesie `usage`.
- Brak `model`: 400. Agent bez pakietu: 404 `model_not_found`. Pakiet wyczerpany: 429 `insufficient_quota`.

### `POST /v1/tasks`

```json
{"model":"demo-celina-przykladowska","offering_id":"<guid pakietu>","input":"<tekst, 20 do 20000 znaków>"}
```

Wszystkie trzy pola są wymagane (`offering_id` to id **oferty** z profilu agenta, nie id zakupu). Odpowiedź 202: `{"id":"<guid>","status":"queued"}`. Jednostka jest zdejmowana od razu. Brak pakietu `Tasks` tej oferty: 404 `model_not_found`; wyczerpany: 429 `insufficient_quota`.

### `GET /v1/tasks/{id}`

Tylko właściciel (inaczej 404 `task_not_found`):

```json
{"id":"...","status":"queued|running|succeeded|failed","output":null,"created_at":"2026-11-24T10:00:00Z","completed_at":null}
```

`status` jest pisany małymi literami. `output` to tekst odpowiedzi agenta i jest `null`, dopóki zadanie się nie powiodło (także dla `failed`).

### Te same dane w panelu (token użytkownika, nie klucz)

`POST /api/v1/bookings/{id}/tasks` (ciało `{"input":"..."}`, 202; 422 `validation_failed` dla złej długości, 409 `invalid_transition`, gdy pakiet nie jest aktywnym pakietem `Tasks` z wolną jednostką), `GET /api/v1/me/tasks/{id}` i `GET /api/v1/me/bookings/{bookingId}/tasks` (tablica zadań, od najnowszego, ten sam kształt co wyżej).

## Wykonawca zadań

`AgentTaskJob` (w usłudze Booking) co `Jobs:AgentTasks:IntervalSeconds` sekund (domyślnie 5) bierze do 5 najstarszych zadań `queued` i oznacza je jako `running` (jedno warunkowe `UPDATE` na zadanie, więc dwie instancje nie wykonają tego samego zadania). Dla każdego woła `IAgentUpstream.CompleteAsync` z `stream: false`, jedną wiadomością `user` (treść zadania) i polem `metadata`: `{"gabinety_offering_id":"<guid>","gabinety_task_id":"<guid>"}`. Tekst z `choices[0].message.content` staje się `output`, a zadanie `succeeded`.

Błąd (status inny niż 2xx, wyjątek, odpowiedź bez tekstu): zadanie wraca do kolejki; po **3 startach** (`Attempts`) staje się `failed`, a jednostka wraca do pakietu. Zadanie `running` dłużej niż `Agents:TaskTimeoutMinutes` (domyślnie 30) liczy się jak błąd. Platforma jest wyznaczana dopiero wtedy, gdy w kolejce jest zadanie.

**Nieudane zadanie po auto-zakończeniu pakietu** (decyzja właściciela, ADR 0013). Pakiet `Tasks` przechodzi w `Completed`, gdy `UnitsUsed >= UnitsTotal` (`AutoCompleteJob`, co 60 s). Jeśli ostatnie zadanie potem zakończy się `failed`, jednostka wraca (`EntitlementStore.RefundUnitsAsync`), a `AgentTaskJob` **przywraca pakiet do `Confirmed`** (`PackageReopening`, wyzwalacz `UnitRefunded`), żeby klient mógł jej użyć. Robi to w jednej transakcji z blokadą wiersza (`AccessTransitions`, tak jak `AutoCompleteJob`) i **tylko gdy wszystko poniższe jest prawdą** (sprawdzane pod blokadą):

- pakiet jest `Completed` (nie `Disputed`, `Resolved` ani anulowany), rodzaju `Tasks`, i ma wolną jednostkę (`UnitsUsed < UnitsTotal`);
- ważność się nie skończyła (`EndUtc > teraz`): pakiet zakończył się wyczerpaniem jednostek, a nie upływem czasu. Po `EndUtc` status zostaje, jednostka mimo to wraca;
- nic nie wypłacono: nie ma wiersza `Payout` (w żadnym stanie, także `Pending`) ani wpisu księgi `PractitionerPayout`.

Skutek: status `Confirmed`, `EndedAt`, `PayoutDueAt`, `FeeMinor` i `PayoutMinor` puste, a **niewypłacony** wpis księgi `PlatformFee` usunięty (jak przy reklamacji: `MoneyReopening`, wzór `ReopenMoney`), co zwalnia parę klient-agent dla zakupu, który naprawdę zarobi prowizję. Gdy warunki nie są spełnione (wypłacono, reklamacja, minął `EndUtc`), pakiet zostaje, jak dziś. Jednostka wraca i pakiet otwiera się dwoma kolejnymi zatwierdzonymi krokami (zwrot, potem otwarcie); równoległy `AutoCompleteJob` albo zakończy pakiet przed zwrotem (wtedy otwarcie go cofnie), albo zobaczy wolną jednostkę i go nie zakończy (`IsDue` jest sprawdzane pod blokadą). Przerwanie procesu między krokami zostawia stan jak przed zmianą (`Completed` z wolną jednostką, droga reklamacji otwarta). Na InMemory (profil `fake`, testy) blokadę zastępuje jedna bramka procesu dla `AccessTransitions` i `EntitlementStore`. Test wyścigu: `PackageReopeningTests` (InMemory) i `PackageReopeningIntegrationTests` (PostgreSQL).

Treść zadania i odpowiedź są tekstem użytkownika: są zapisane w bazie i zwracane właścicielowi, nigdy nie trafiają do logów ani komunikatów błędów. Usunięcie danych użytkownika czyści `InputText` i `OutputText`.

## Kontrakt dostawcy agentów

Ta aplikacja nie uruchamia agentów. Woła zewnętrzną platformę przez `IAgentUpstream` (`src/Gabinety.Booking/Agents`):

```csharp
Task<UpstreamResult> CompleteAsync(string agentSlug, JsonObject body, CancellationToken ct); // UpstreamResult(StatusCode, Json, TotalTokens)
Task<long> StreamAsync(string agentSlug, JsonObject body, Stream output, CancellationToken ct); // zwraca tokeny z ostatniego fragmentu usage
```

`StreamAsync` rzuca `UpstreamException`, gdy dostawca odpowie błędem albo nie odpowie, zanim zapisano pierwszy bajt.

Implementacja `Http` (`HttpAgentUpstream`) wysyła:

- `POST {Agents:Upstream:BaseUrl}/v1/chat/completions`, więc `BaseUrl` to adres główny platformy **bez** `/v1`;
- `Authorization: Bearer {Agents:Upstream:ApiKey}`;
- ciało żądania klienta z `model` = slug agenta; `stream` ustawione na `true` albo `false` zależnie od wywołania; przy strumieniu `stream_options.include_usage = true`.

Od platformy wymaga: odpowiedzi zgodnej z OpenAI Chat Completions, `usage.total_tokens` w odpowiedzi (przy strumieniu w ostatnim fragmencie `data:`), przepuszczania pól `tools` i `tool_choice` oraz strumienia SSE. Czas jednego wywołania ogranicza `Agents:Upstream:TimeoutSeconds`.

Implementacja `Fake` (`FakeAgentUpstream`, bez sieci, deterministyczna): odpowiedź `[demo <slug>] Otrzymano N znaków.` (N to łączna długość treści wiadomości; treść nie jest powtarzana), `total_tokens = N / 4 + 20`; strumień to trzy fragmenty, fragment z `usage` (bez `choices`) i `data: [DONE]`.

## Konfiguracja

Zmienne środowiskowe mają `__` zamiast `:`. Wszystkie klucze są wpisane w `deploy/config-keys.map`.

| Klucz | Domyślnie | Znaczenie |
|---|---|---|
| `Agents:Provider` | brak | `Fake` (tylko profil `fake`: `GABINETY_FAKE=1` lub `Environment:Kind=Development`) albo `Http`. Inna wartość zatrzymuje start hosta. |
| `Agents:Upstream:BaseUrl` | brak | adres platformy (bezwzględny `http` lub `https`); wymagany przy `Http`, inaczej host się nie uruchamia |
| `Agents:Upstream:ApiKey` | brak | **sekret** (`Agents__Upstream__ApiKey`); wymagany przy `Http`; nie trafia do logów ani komunikatów błędów |
| `Agents:Upstream:TimeoutSeconds` | 600 | limit czasu jednego wywołania platformy; wartość niedodatnia lub nieliczbowa daje 600 |
| `Agents:TaskTimeoutMinutes` | 30 | po tylu minutach zadanie `running` liczy się jak błąd |
| `Jobs:AgentTasks:IntervalSeconds` | 5 | jak często wykonawca bierze zadania (liczba większa od 0 i mniejsza od 86400) |
| `Jobs:Enabled` | włączone | `false` wyłącza wszystkie zadania w tle, także wykonawcę zadań agentów |
| `Access:ValidityDays` | 30 | ważność zakupionego pakietu w dniach, liczona od zakupu; kod nie sprawdza wartości, podaj liczbę dodatnią (stała z sekcji 9 kontraktów, należy do właściciela) |
| `Access:ReportedOpenSlots` | 10 | stała wysyłana do rankingu jako `open_slots_14d` (agent jest zawsze dostępny) |

Rejestracja dostawcy (`AgentsFeature`): w profilu `fake` brak `Agents:Provider` oznacza `Fake`. **Poza profilem `fake` host bez `Agents:Provider` startuje**, ale nie ma platformy: pierwsze użycie (żądanie `/v1/chat/completions` albo zadanie w kolejce) kończy się błędem konfiguracji. Zaostrzenie (wdrożony host bez ustawienia nie startuje) sprawdzono i wycofano, bo psuje 26 testów profilu wdrożonego w `Gabinety.Booking.Tests` (m.in. `PublicEdgeTests`, `RouteAuthTests`, `ClockApiTests`), które uruchamiają host bez platformy. Zamiast tego konfiguracja wdrożenia wymusza ustawienia: `deploy/fly/booking.fly.toml` ma `Agents__Provider = "Http"` i `Agents__Upstream__BaseUrl` (adres do uzupełnienia przez właściciela; wdrożenie odmawia konfiguracji z placeholderem), sekret `Agents__Upstream__ApiKey` jest w `deploy/secrets.map` jako wymagany (GitHub secret `AGENTS_UPSTREAM_API_KEY`), a `deploy/config-keys.map` oznacza wszystkie trzy jako `required` (`scripts/check-config-coverage.sh`). Opis wdrożenia: `docs/operations/DEPLOY.md`.

## Gdzie `/v1` jest dostępne

Klient ma **jeden adres**: `Site:BaseUrl` + `/v1` (profil `fake`: `http://localhost:3000/v1`). API obsługuje usługa Booking, a strona (`src/Gabinety.Web`, `AgentApiProxy`) przekazuje do niej `/v1/*` (decyzja właściciela, ADR 0013). Booking nadal jest dostępny pod własnym adresem (profil `fake`: `http://localhost:5102/v1`; w produkcji `PublicEdge` przepuszcza `/v1` z internetu), ale klientom podaje się adres strony.

### Co robi przekazywanie przez stronę

- **Przekazuje** metodę, ścieżkę z zapytaniem i ciało **strumieniowo** (bez buforowania; do 2 MB jak w Bookingu: większe ciało to 413 `request_too_large`, także gdy nie ma `Content-Length`) oraz tylko cztery nagłówki: `Authorization`, `Idempotency-Key`, `Content-Type`, `Accept`.
- **Nie przekazuje** ciasteczek (sesja strony nie ma znaczenia dla API, które uwierzytelnia klucz), nagłówków hop-by-hop ani żadnych innych nagłówków klienta. `Set-Cookie` z odpowiedzi też jest odrzucany.
- **Odpowiedź** wraca strumieniowo, kawałek po kawałku (strumień SSE płynie na bieżąco), z kodem statusu i nagłówkami Bookinga (m.in. `Retry-After`, `WWW-Authenticate`) bez nagłówków hop-by-hop. Treść błędu 401/429 to JSON Bookinga, nie strona HTML.
- **Adres klienta** idzie nagłówkami `X-Client-Address` i `X-Edge-Key` (ten sam mechanizm co przy wszystkich wywołaniach strony do usług; `RateLimit:TrustedProxyKey` musi być taki sam w Web i Bookingu). **Limit per klucz** jest bez zmian: liczy go Booking; adres służy tylko żądaniom, których nie da się przypisać do klucza.
- **Booking niedostępny** (brak połączenia, przekroczony czas połączenia): 502 w kształcie OpenAI (`error.code = upstream_error`, `type = api_error`, stały tekst „The API service did not answer.”). Nie ma ponowień (ciała strumieniowego nie da się powtórzyć) ani przerwania po 15 s: strumień może trwać, aż skończy go dostawca albo klient.
- **Ścieżka** nie może wyjść poza `/v1` (segmenty `.` i `..` nie są przekazywane); `/v10` nie jest API.
- **Bez logowania** ciała, nagłówka `Authorization` i treści: w logu jest tylko metoda, status, czas i typ wyjątku.
- **Poza ochroną przeglądarki:** `/v1` jest wyjęte spod filtra Origin (`BrowserOriginGuard`), antiforgery, uwierzytelniania ciasteczkiem, stron statusów i pamięci podręcznej odpowiedzi; to klienci spoza przeglądarki, uwierzytelniani kluczem. W tabeli tras (`ROUTE-AUTH.md`) trasa to jeden wiersz `ANY /v1/{**path}`, Anonymous po stronie strony (klucz sprawdza Booking).
- **Sieć:** strona woła Booking po nazwie logicznej `booking` (`Backend:Booking`), przez sieć prywatną, tak jak każde inne wywołanie; wdrożenie nie wymaga zmian (`docs/operations/DEPLOY.md`).
