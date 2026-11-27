# ADR-0013 (propozycja): marketplace agentów AI zamiast konsultacji

- Status: **proponowana**, do potwierdzenia przez właściciela. Pierwsze osiem decyzji to propozycje z sekcji 4 planu (`docs/agentic/PLAN.md`); sekcja „Decyzje podjęte w trakcie” opisuje to, co wykonawcy faktycznie zdecydowali w PR #2 do #5; sekcja „Decyzje właściciela” zbiera cztery pytania, na które kod nie odpowiadał: właściciel rozstrzygnął trzy (adres `/v1`, nieudane ostatnie zadanie po auto-zakończeniu, odświeżanie listy zadań; w jego zleceniu to punkty 1, 2 i 3), czwarta (rozłączony strumień czatu) jest otwarta. Reszta dokumentu pozostaje propozycją do potwierdzenia.
- Data: 2026-11-24.
- Zadanie: D-03 (5). Opis oparty na kodzie `main` po D-02 i na opisach scalonych PR #2, #3, #4 i #5. Repozytorium specyfikacji (`gabinety-tech-spec`) jest tylko do odczytu dla wykonawcy, więc propozycja leży tutaj; stałe z sekcji 9 kontraktów (np. ważność pakietu) należą do właściciela.
- Buduje na: ADR-0009 (kod w tym repozytorium, specyfikacja w osobnym), ADR-0010 (frontend Blazor), propozycje 0011 (Stripe Connect) i 0012.

## Kontekst

Gabinety Tech sprzedawały konsultacje z programistami (termin, spotkanie, opinia). Gabinety Agents sprzedaje **dostęp do agentów AI**: klient kupuje pakiet i korzysta z niego przez panel albo przez API zgodne z OpenAI (np. GitHub Copilot BYOK). Agentów uruchamia zewnętrzna platforma; ta aplikacja jest katalogiem, sklepem, licznikiem zużycia i bramą API. Nazwy w kodzie zostają dawne (`Practitioner` to agent, `Offering` to pakiet, `Booking` to zakupiony pakiet; zmiana nazw to opcjonalny etap E6).

## Decyzje z planu (sekcja 4) i jak wyglądają w kodzie

| Nr | Decyzja | Stan w kodzie |
|---|---|---|
| 1 | Ważność pakietu: 30 dni | `Access:ValidityDays` (domyślnie 30), liczone od zakupu w Booking (`CreateBookingFeature`). **Strona nie czyta tego ustawienia**: „Ważność: 30 dni” na profilu jest stałą tekstową w `src/Gabinety.Web` (`PlPages.Profile.ValidityDays`). Zmiana ustawienia bez zmiany tekstu da rozbieżność. |
| 2 | Rezygnacja: pełny zwrot tylko przed pierwszym użyciem | `CancelPlanner`: dozwolona w stanie `Confirmed` przy `UnitsUsed == 0`, zawsze pełny zwrot; później 409 `invalid_transition`. Reguły 24 h już nie ma (klasa `RefundPolicy` została, Booking jej nie woła). |
| 3 | Rozliczanie czatu: suma tokenów wejścia i wyjścia, jedna stawka | z `usage.total_tokens` odpowiedzi dostawcy, po fakcie; ostatnie żądanie może przekroczyć pulę (`UnitsUsed` > `UnitsTotal`). Zakup pakietu `Chat` mnoży jednostki przez 1000. |
| 4 | Do 5 kluczy na konto, wspólne dla wszystkich agentów | `ApiKeysFeature.MaxActive = 5`; klucz identyfikuje konto, agenta wybiera pole `model`. |
| 5 | `/v1` w usłudze Booking | tak (`PublicEdge` przepuszcza `/v1`), a strona przekazuje je pod swoim adresem (`AgentApiProxy`); patrz „Decyzje właściciela” 1. |
| 6 | Jedno konto techniczne na agenta | zdjęte w E-04 (jedno konto, do `Directory:MaxAgentsPerProvider` agentów); patrz „Decyzja E-04” niżej. |
| 7 | Wypłaty i prowizja bez zmian w profilu `fake`, do usunięcia przed prawdziwymi płatnościami | bez zmian; termin wypłaty zmieniony (niżej). Zakres usunięcia: `docs/operations/PAYMENTS-AGENTS.md`. |
| 8 | Plakietka „Agent AI” wszędzie, gdzie widać nazwę agenta | komponent `AiBadge` na karcie, profilu, w podsumowaniu zakupu, na liście i w szczegółach dostępu; J1 sprawdza kartę i profil. |

## Decyzje podjęte w trakcie (PR #2 do #5)

Zapisane tu, żeby nie ginęły w opisach PR. Każdą można wycofać osobno.

**Model danych i cykl życia (PR #2).**
- Termin wypłaty to `EndUtc + 72 h`. Wcześniejsza reguła `max(EndUtc + 48 h, StartUtc + 72 h)` mogłaby wypłacić pakiet 30-dniowy do 24 godzin przed zamknięciem okna reklamacji. Decyzja właściciela: „koniec pakietu plus 72h”. Jest w osobnym commicie.
- Zakup ignoruje `startUtc` (pole jest opcjonalne; nieparsowalna wartość daje 400, poprawna jest ignorowana). `StartUtc` to moment zakupu.
- Blokadę po praktyku zastąpiła procesowa blokada po `Idempotency-Key` (`KeyLocks`, tylko InMemory); na PostgreSQL decyduje unikalny indeks.
- Pakiety sprzed zmiany mają `UnitsTotal = 0`, więc zakończenie z wyczerpania wymaga `UnitsTotal > 0`.
- Przypomnienia o terminie usunięte (start = zakup). `OpenSlots` dla rankingu to stała `Access:ReportedOpenSlots` (10).
- `SlotCalculator` nie czyta już zakupów; endpoint slotów i kod terminów usunięte w E-01.
- Wymagany SDK: co najmniej 10.0.100 z naprawionym Razorem; `global.json` ma `rollForward: latestFeature`, a sesje używały 10.0.404.

**API i zużycie (PR #3).**
- Limit per klucz bez zmian w jądrze (jądro `ServiceDefaults` jest ograniczone testem architektury do 400 linii): limiter widzi `sub = apikey:{id}`, a autoryzacja potem podmienia tożsamość; odmowa w kształcie OpenAI to `PostConfigure` na `OnRejected` w Booking.
- Zadania zdejmują jednostkę **warunkowo** (`TryConsumeAsync`: `UPDATE ... WHERE units_used + n <= units_total`), a nie bezwarunkowo jak czat, bo zadanie to cała jednostka i nie wolno przekroczyć puli.
- `offering_id` w `POST /v1/tasks` jest wymagane; ciało większe niż 2 MB to 413.
- Nieudane zadanie zwraca jednostkę i dopisuje `UsageRecord` z `Units = -1`; usuwanie danych zamienia też `ClientUserId` zadań na token.
- `AgentsFeature` pozwala wdrożonemu hostowi bez `Agents:Provider` wystartować (błąd dopiero przy pierwszym użyciu). Zaostrzenie sprawdzono w D-03: patrz „Zaostrzenie `AgentsFeature`” niżej.

**Interfejs (PR #4).**
- Adres bazowy API na profilu to `Site:BaseUrl` + `/v1`, a przy płatnościach próbnych adres Bookinga + `/v1` (`WebOptions.ApiBaseUrl`); bez nowego klucza konfiguracji.
- Kształt listy zadań (`GET /api/v1/me/bookings/{id}/tasks`) nie był w planie: tablica obiektów `{id, status, output, created_at, completed_at}`.
- Adresy stron zmieniono w E-02: `/programisci` na `/agenci`, `/gabinet/{slug}` na `/agent/{slug}`, `/rezerwacja/...` na `/dostep/...`, `/moje/rezerwacje` na `/moje/dostepy`, `/gabinet-programisty` na `/panel-agenta`; stare adresy odpowiadają 301 z zachowanym zapytaniem (`LegacyRedirects`). Napisy „gabinet” w tekstach strony zostają.

**Dane demonstracyjne i ścieżki (PR #5).**
- Zużycie pakietów w seederze powstaje przez prawdziwe `POST /api/v1/bookings/{id}/tasks`, nie przez zapis do bazy; seeder nie rusza zegara testowego.
- Trzy poprawki Webu znalezione przez J3 i J8: nazwy pól `created_at`/`completed_at` (lista pokazywała „Zlecone: 1 stycznia 1”), rodzaj pakietu pisany małymi literami w publicznym JSON (pakiet czatu pokazywał „500 zadań”), cofnięta próba `forceLoad`.
- J8 nie zakładał, że `/v1` jest pod adresem strony: brał adres z potwierdzenia zakupu i wołał Booking wprost. Od decyzji 1 (niżej) wszystkie wywołania idą przez stronę, a jedno z nich jest powtórzone na Bookingu dla porównania.
- Zużycie czatu w panelu to tysiące tokenów dzielone całkowicie, więc małe użycie to „wykorzystano 0 z 500 tys. tokenów”. To decyzja o prezentacji, nie błąd.
- Znany wyścig na InMemory (profil `fake`): `AutoCompleteJob` i równoległa reklamacja pakietu o wyczerpanych jednostkach; na PostgreSQL chroni blokada wiersza.

## Zaostrzenie `AgentsFeature` (D-03)

Pytanie z zadania: czy wdrożony host bez `Agents:Provider` ma nie startować? Sprawdzone przez zmianę `AgentsFeature` tak, by brak ustawienia poza profilem `fake` zatrzymywał start (usunięty warunek `!string.IsNullOrWhiteSpace(provider)` przy filtrze startu). Wynik: `dotnet test --filter "tier!=integration"` miał **26 porażek** w `Gabinety.Booking.Tests` (m.in. `PublicEdgeTests`, `RouteAuthTests`, `ClockApiTests`, `MockPaymentsFailClosedTests`, `SmtpWiringTests`, `AgentTaskJobTests.A_deployed_host_without_a_platform_starts...`), bo testy profilu wdrożonego uruchamiają host bez platformy. Zmianę **wycofano**. Zamiast niej konfiguracja wdrożenia wymusza ustawienia: `Agents__Provider = "Http"` i adres platformy w `booking.fly.toml`, wymagany sekret `AGENTS_UPSTREAM_API_KEY` w `secrets.map` i wiersze `required` w `config-keys.map`. Host bez tych ustawień nie zostanie wdrożony (wdrożenie odmawia placeholdera i brakującego sekretu), a przypadkowy start bez nich daje błąd przy pierwszym użyciu. Gdyby właściciel chciał twardego startu w kodzie, trzeba najpierw dać testom profilu wdrożonego atrapę platformy.

## Decyzje właściciela

Decyzje 1, 2 i 4 właściciel rozstrzygnął (opisane niżej wraz z tym, co zrobiono); decyzja 3 (rozłączony strumień czatu) jest dalej otwarta, a jej rekomendacja to propozycja wykonawcy, nie rozstrzygnięcie.

### 1. Adres `/v1`: ROZSTRZYGNIĘTE (właściciel): pod adresem strony (opcja B)

Pytanie: sekcja „Jak się połączyć” podawała klientowi `Site:BaseUrl` + `/v1`, a strona nie miała trasy `/v1` (odpowiadał tylko Booking), więc przykład `curl` na wdrożeniu z prawdziwymi płatnościami wskazywał adres, który go nie obsługuje. Opcje z propozycji: A (osobny adres API, np. `api.<domena>`), B (strona przekazuje `/v1`), C (osobna usługa API, E-05). Proponowałem A; **właściciel wybrał B**: klient ma jeden adres.

**Co zrobiono** (`src/Gabinety.Web/Hosting/AgentApiProxy.cs`, bez nowej zależności: `HttpClient` z nazwą logiczną `booking`, czyli `Backend:Booking`, strumieniowanie w obie strony; budżet zależności z ADR-0010 bez zmian):
- trasa `ANY /v1/{**path}` przekazuje metodę, ścieżkę z zapytaniem i ciało **strumieniowo** (do 2 MB jak w Bookingu, także bez `Content-Length`; 413 w kształcie OpenAI) oraz tylko `Authorization`, `Idempotency-Key`, `Content-Type`, `Accept`. **Nie** przekazuje ciasteczek (sesja strony) ani nagłówków hop-by-hop, ani żadnych innych nagłówków klienta;
- odpowiedź wraca **strumieniowo, bez buforowania** (każdy kawałek jest opróżniany od razu: SSE płynie na bieżąco), z kodem i nagłówkami Bookinga (`Retry-After`, `WWW-Authenticate`), bez hop-by-hop i `Set-Cookie`. Booking niedostępny: 502 `upstream_error` w kształcie OpenAI;
- adres klienta: istniejącym mechanizmem `X-Client-Address` + `X-Edge-Key` (`BackendHeadersHandler`; Booking czyta adres przez `RateLimits.PartitionKey`, nie przez `Fly-Client-IP`). Limit per klucz bez zmian w Bookingu (liczy go Booking po `sub = apikey:{id}`);
- nic z ciała, nagłówka `Authorization` ani treści nie trafia do logów;
- `/v1` jest wyjęte spod filtra Origin, antiforgery, uwierzytelniania ciasteczkiem i pamięci podręcznej odpowiedzi (to klienci spoza przeglądarki, uwierzytelniani kluczem), a strony statusów są wyłączone dla jego odpowiedzi (błąd dostawcy nie zamienia się na stronę HTML). Reszta ścieżek ma potok bez zmian (testy);
- `ROUTE-AUTH.md` i `RouteAuditTests`: jeden nowy wiersz `ANY /v1/{**path}`, Anonymous po stronie strony (klucz sprawdza Booking), jedyna trasa pod `/v1`; test pilnuje też, że antiforgery jej nie dotyczy;
- adres w sekcji „Jak się połączyć” to zawsze `Site:BaseUrl` + `/v1` (usunięty wyjątek profilu z płatnościami próbnymi, który podawał adres Bookinga); przykłady `curl` w profilu `fake` wskazują `http://localhost:3000/v1`;
- J8 w `tests/e2e` woła stronę (adres z potwierdzenia zakupu = `Site:BaseUrl` + `/v1`), a jedno wywołanie powtarza na Bookingu dla porównania;
- wdrożenie: bez zmian ustawień. `web-blazor.fly.toml` już woła Booking po sieci prywatnej (`Backend__Booking = ….internal:8080`) i ma `RateLimit__TrustedProxyKey`; `check-private-topology.sh` nie wymaga zmian. Dodano komentarze (strumień zajmuje połączenie liczone w `[http_service.concurrency]`).

**Skutki i ryzyka.** Web jest teraz częścią ścieżki API: jego restart przerywa strumienie w toku, a długi strumień zajmuje jedno z 150 połączeń maszyny 512 MB (limit połączeń jest ustawiony pod okna przeglądarki, `docs/operations/PERFORMANCE.md`; ruch API nie był mierzony). Proxy nie ponawia żądań (ciała strumieniowego nie da się powtórzyć) i nie ma całkowitego limitu czasu odpowiedzi (jest tylko 5 s na połączenie i 15 min do nagłówków). Gdy ruch API zacznie obciążać stronę, opcja C (E-05) jest dalej otwarta. Booking nadal odpowiada na `/v1` pod własnym adresem, ale klientom podaje się adres strony.

### 2. Ostatnie zadanie `failed` po auto-zakończeniu pakietu: ROZSTRZYGNIĘTE (właściciel): opcja B, zwrot jednostki cofa pakiet do `Confirmed`

Pytanie: `AutoCompleteJob` przenosi pakiet `Tasks` w `Completed`, gdy `UnitsUsed >= UnitsTotal`; jeśli ostatnie zadanie potem kończy się `failed`, jednostka wraca, ale pakiet już jest `Completed`, więc klient ma wolną jednostkę, której nie może użyć. Proponowałem C (nie kończyć pakietu, dopóki są zadania `queued` lub `running`); **właściciel wybrał B**.

**Co zrobiono** (`PackageReopening`, `AgentTaskJob.FailAsync`):
- nowy wyzwalacz `BookingTrigger.UnitRefunded` i jedno nowe przejście `Completed` -> `Confirmed` w `BookingStateMachine` (test tabeli rozszerzony, wyczerpujące sprawdzenie innych par dalej przechodzi);
- po zwrocie jednostki zadanie w tle woła `PackageReopening.TryAsync`: jedna transakcja z blokadą wiersza przez `BookingTransitions`, z regułami sprawdzanymi **pod blokadą**: pakiet `Completed` rodzaju `Tasks` z wolną jednostką, `EndUtc > teraz`, brak wiersza `Payout` (każdy stan) i wpisu `PractitionerPayout` (nowa `TransitionRequest.DbPrecondition`, sprawdzenie z bazą w transakcji). Inaczej nic się nie zmienia (wypłacono, `Disputed`/`Resolved`, minął `EndUtc`);
- skutek: `Confirmed`, `EndedAt`, `PayoutDueAt`, `FeeMinor`, `PayoutMinor` puste i usunięty **niewypłacony** wpis `PlatformFee`. `FeeHook` zostawia wiersz księgi przy zakończeniu (nalicza prowizję i pisze `PlatformFee`), więc cofnięcie robi to samo co reklamacja: wspólna `MoneyReopening.Reopen` (wzór `ReopenMoney` z `DisputeFeature`, wydzielony i używany w obu miejscach), a hook zwalnia zajętą parę w zastępniku InMemory tylko wtedy, gdy ten pakiet ją miał. Para jest wolna dla zakupu, który naprawdę zarobi prowizję (test: po ponownym wyczerpaniu pakiet znów ma jeden wiersz `PlatformFee`);
- wyścig z `AutoCompleteJob`: zwrot jednostki i otwarcie to dwa zatwierdzone kroki w tej kolejności; `AutoCompleteJob` sprawdza `IsDue` pod blokadą wiersza, więc albo zakończy pakiet przed zwrotem (otwarcie go cofnie), albo zobaczy wolną jednostkę i go nie zakończy. Na InMemory nie ma blokady wiersza, więc `BookingTransitions` i `EntitlementStore` dzielą jedną bramkę procesu (bez niej test wyścigu pada w kilku rundach; sprawdzone). To usuwa też znany wyścig InMemory opisany wyżej dla `AutoCompleteJob` i równoległej reklamacji (zmiana dotyczy tylko InMemory);
- testy: `PackageReopeningTests` (InMemory: reguła na wierszu, cała ścieżka, ponowne zakończenie z prowizją, dwie jednostki, po `EndUtc`, wypłacono lub w trakcie, `Disputed`/`Resolved`/anulowany, reklamacja po zakończeniu, 40 rund wyścigu) i `PackageReopeningIntegrationTests` (`tier=integration`, PostgreSQL 17: ścieżka z prawdziwym SQL, blokada przez `Payout` i wpis wypłaty, 25 rund dwóch zadań zakończenia i nieudanego zadania naraz).

**Ograniczenia.** Zwrot jednostki i otwarcie to dwa kroki: przerwanie procesu między nimi zostawia `Completed` z wolną jednostką (jak przed zmianą; klient może zgłosić reklamację do `EndUtc + 72 h`). Nie rusza ścieżki `TasksFeature.QueueAsync` (zwrot, gdy nie da się zapisać zadania w tym samym żądaniu). Opcja C (nie kończyć pakietu przy zadaniach w toku) nie została zrobiona i nie jest potrzebna, dopóki B działa; mogłaby dojść jako dodatkowa osłona.

### 3. Rozliczanie rozłączonego strumienia czatu

Dziś nic nie jest naliczane, gdy klient rozłączy się przed końcem strumienia: `IAgentUpstream.StreamAsync` zwraca liczbę tokenów dopiero po całym strumieniu, a w `finally` rozlicza się to, co zwrócił (0). Platforma mogła wykonać pracę, więc klient dostaje ją za darmo (dotyczy rozłączenia przed fragmentem `usage`).

| Opcja | Skutek |
|---|---|
| A. Zostawić | prosto; ryzyko nadużycia: rozłączanie przed fragmentem `usage` daje darmowy czat |
| B. Podsłuchiwać fragment `usage` w strumieniu wyjściowym i rozliczać ostatnią znaną wartość | zmiana kontraktu `IAgentUpstream` (B-03); `usage` przychodzi na końcu, więc rozłączenie w środku nadal nic nie da |
| C. Rozliczyć oszacowanie (np. liczba znaków przesłanych klientowi / 4 + wejście) po rozłączeniu | zawsze coś naliczy; to szacunek, nie dane dostawcy |
| D. Wymagać od platformy kontynuacji zadania po rozłączeniu i odczytać `usage` osobnym zapytaniem | zależy od możliwości platformy, której kontraktu nie znamy |

Rekomendacja: C z oznaczeniem w `UsageRecord`, że wartość jest szacunkiem, albo A, jeśli ryzyko nadużycia jest małe (pakiet jest opłacony z góry i ważny 30 dni). Decyzja zależy od tego, co platforma agentów zwraca przy zerwanym połączeniu.

### 4. Odświeżanie listy zadań: ROZSTRZYGNIĘTE (właściciel): opcja B, bez napisu i bez meta refresh, zostaje „Odśwież listę”

Pytanie: `BookingDetail.razor` ustawiał `<meta http-equiv="refresh" content="5">`, gdy były zadania `queued` lub `running`, i pokazywał „Zadanie jest w toku. Strona odświeża się sama.” Po zleceniu zadania następuje nawigacja rozszerzona (enhanced navigation), po której meta refresh nie działa, tak samo dla adresu z fragmentem `#zadania`, więc napis nie był prawdą. Dodatkowo axe zgłaszał `meta-refresh` jako błąd krytyczny (WCAG 2.2.1) w stanie „w toku”. Proponowałem B teraz i C (skrypt odświeżający listę, wyłączalny przez użytkownika) później; **właściciel wybrał B**.

**Co zrobiono:**
- z `BookingDetail.razor` usunięty `<meta http-equiv="refresh">`; tekst to teraz „Zadanie jest w toku.” z linkiem „Odśwież listę” (zwykły link do tej samej strony, działa bez skryptu). Strona potwierdzenia zakupu zachowuje własny meta refresh na czas oczekiwania na płatność (inna sprawa: tam nawigacja jest zwykła, a napis jest prawdziwy);
- test strony (`TasksAndKeysTests`): brak `meta[http-equiv=refresh]` przy zadaniu w kolejce, brak napisu „odświeża się sam”, link „Odśwież listę” prowadzi na stronę dostępu;
- e2e: J5 i J6 nie czekają już na wykonanie zadania tylko po to, by ominąć axe (axe ogląda stronę ze zleceniem „w kolejce”), J3 dodatkowo sprawdza axe zaraz po zleceniu; J3 nadal czeka na odpowiedź agenta (przez „Odśwież listę”), bo sprawdza jej treść. Komentarz w `waitForAnswer` poprawiony.

**Zostaje.** Klient widzi wynik po kliknięciu „Odśwież listę”; jeśli będzie czekał na wyniki dłużej niż kilkadziesiąt sekund, opcja C (skrypt odświeżający, wyłączalny) pozostaje do zrobienia.

## Decyzja E-04: jeden dostawca, wielu agentów (propozycja wykonawcy do potwierdzenia)

Plan, decyzja 6, zakładał „jedno konto techniczne na agenta” i błąd `practitioner_exists`. E-04 to zdejmuje: **jedno konto użytkownika (dostawca) prowadzi wielu agentów**, każdy z własnym profilem, slugiem, pakietami i statusem zatwierdzenia. Nazwy w kodzie bez zmian (`Practitioner` = agent; zmiana nazw to E-06).

**Model.** `practitioners.user_id` przestaje być unikalne (zwykły indeks; migracja `20261128100000_E-04_ManyAgentsPerProvider`, osobna, starych nie ruszano). Slug zostaje globalnie unikalny. Nazwa wyświetlana nie była i nie jest unikalna w bazie (dzisiejsza reguła to tylko unikalny slug; nic nie dodałem).

**Limit.** `Directory:MaxAgentsPerProvider` (domyślnie 10; wartość poniżej 1 to domyślna; wiersz `optional` w `deploy/config-keys.map`). Przekroczenie: 422 `too_many_agents` w kształcie `problem+json` jak `too_many_offerings`. Na PostgreSQL liczenie i wstawienie są jednym krokiem pod blokadą doradczą na id użytkownika, więc dwa równoległe żądania nie przekroczą limitu.

**API (Directory).** `POST /api/v1/me/practitioner` zawsze tworzy **nowego** agenta (201). Nowe `GET /api/v1/me/practitioners` (lista własnych, od najstarszego). Wywołania działające na jednym agencie (`GET`/`PUT /me/practitioner`, `submit`, `offerings...`, `github-link...`) biorą slug w `?agent=`. Konto z jednym agentem może pominąć parametr (zachowanie sprzed E-04: „użytkownik z jednym agentem widzi to samo bez dodatkowego kroku”); przy dwóch i więcej parametr jest wymagany (422 `validation_failed` na polu `agent`), bo ciche wybranie jednego mogłoby zmienić nie tego agenta. Cudzy albo nieznany slug to 404 (nie ujawnia, że istnieje). `GET /api/v1/me` dostaje `agents` (lista), a `practitionerId`/`practitionerStatus` opisują pierwszego (najstarszego) agenta, więc klient napisany na jednego agenta działa bez zmian. Wcześniej `/me` zwracał `practitionerId` zawsze `null` (pole nie było wypełniane); teraz jest wypełnione.

**Uprawnienia.** Edytować agenta może tylko właściciel konta (wiersz z `user_id` = `sub` tokenu). Zatwierdza admin **każdego agenta osobno** (trasy administratora już działały po id agenta). **Zawieszenie jednego agenta nie zawiesza pozostałych**: zawieszenie dotyczy treści i sprzedaży konkretnego agenta (jego profil znika z katalogu, jego pakiety nie są sprzedawane, jego przyszłe dostępy są anulowane z pełnym zwrotem); konto dostawcy i inni agenci nie są ukarani za jednego. Jeśli administrator uzna, że problem dotyczy dostawcy, zawiesza każdego agenta osobno. Odrzucenie i ponowne zgłoszenie też są per agent.

**Profil GitHub.** Dawna reguła „jeden zweryfikowany profil GitHub = jeden agent” (unikalny indeks) uniemożliwiłaby wysłanie drugiego agenta do zatwierdzenia bez drugiego konta GitHub, bo zgłoszenie wymaga `github_linked`. Reguła to teraz „jeden profil GitHub = jedno konto dostawcy”: agenci tego samego konta mogą go współdzielić, agent innego konta nie. Indeks po `lower(github_login)` jest zwykłym indeksem częściowym, a sprawdzenie robi aplikacja (`VerificationFeature`). Utracony jest bezpiecznik bazy przy wyścigu dwóch kont: żeby obie weryfikacje przeszły, kod z wyzwania musi być w opisie profilu GitHub, więc wyścig może wywołać tylko właściciel tego profilu. **Do potwierdzenia przez właściciela**; wycofanie to przywrócenie unikalnego indeksu (przy zdublowanych profilach najpierw trzeba je rozdzielić).

**Konto płatności (Booking).** Zdecydowane: **konto płatności zostaje per agent** (klucz `practitioner_id`, bez zmiany schematu). Powód: wypłaty, `PayoutJob` i sprawdzenie „czy agent może przyjmować pieniądze” przy zakupie już liczą po agencie; wspólne konto na dostawcę wymagałoby nowej tabeli i przepięcia wypłat, a ten kod ma zniknąć w E-03 (sprzedawcą ma być platforma). Cena: dostawca przechodzi onboarding raz na agenta. Wywołania `/api/v1/me/payments/account` biorą `?agent=<slug>` na tych samych zasadach co Directory (jeden agent: bez parametru).

**Powiadomienia i dostępy.** `Booking.PractitionerUserId` to konto dostawcy, więc jego dostępy (wszystkich agentów) widać w jednej liście „jako dostawca”, a e-mail do powiadomień to adres z tokenu zapisany przy agentach (jeden na konto; przy różnicy wygrywa ostatnio zmieniony wiersz). Usunięcie danych konta anonimizuje wszystkich jego agentów po anulowaniu przyszłych dostępów każdego z nich.

**Zgodność wdrożenia.** Nowa trasa wewnętrzna `GET /internal/v1/practitioners/by-user/{userId}/agents`; stara `by-user/{userId}` zostaje i zwraca najstarszego agenta w tym samym kształcie. Migracja zdejmuje tylko ograniczenie, więc można ją wdrożyć przed kodem. Wdrożyć Directory przed Bookingiem.

## Skutki

- Kod i dokumenty opisują agentów AI; stare nazwy w kodzie to dług opisany w etapie E planu.
- Decyzje 1 (adres `/v1`), 2 (nieudane ostatnie zadanie) i 4 (odświeżanie listy) rozstrzygnięte; otwarta zostaje decyzja 3 (uczciwość rozliczenia czatu przy rozłączonym strumieniu).
- Przed prawdziwymi płatnościami trzeba wykonać zmiany z `docs/operations/PAYMENTS-AGENTS.md` (E-03; wymaga nowej decyzji zastępującej propozycję 0011).
- Połączenie z prawdziwą platformą agentów nie zostało sprawdzone: `HttpAgentUpstream` ma testy, ale nie działał z żadną prawdziwą platformą, a kroki GitHub Copilot BYOK są niezweryfikowane (`docs/operations/COPILOT-BYOK.md`).
