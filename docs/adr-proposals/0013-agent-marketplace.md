# ADR-0013 (propozycja): marketplace agentów AI zamiast konsultacji

- Status: **proponowana**, do potwierdzenia przez właściciela. Pierwsze osiem decyzji to propozycje z sekcji 4 planu (`docs/agentic/PLAN.md`); sekcja „Decyzje podjęte w trakcie” opisuje to, co wykonawcy faktycznie zdecydowali w PR #2 do #5; sekcja „Otwarte decyzje właściciela” to pytania, na które kod dziś nie odpowiada.
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
| 5 | `/v1` w usłudze Booking | tak (`PublicEdge` przepuszcza `/v1`). **Strona go nie przekazuje**; patrz otwarta decyzja 1. |
| 6 | Jedno konto techniczne na agenta | bez zmian (`practitioner_exists`); zdjęcie ograniczenia to E-04. |
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
- `SlotCalculator` nie czyta już zakupów; endpoint slotów i kod terminów zostają do E-01.
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
- Adresy stron (`/programisci`, `/gabinet/{slug}`, `/rezerwacja/...`) i napisy „gabinet” zostają do E-02.

**Dane demonstracyjne i ścieżki (PR #5).**
- Zużycie pakietów w seederze powstaje przez prawdziwe `POST /api/v1/bookings/{id}/tasks`, nie przez zapis do bazy; seeder nie rusza zegara testowego.
- Trzy poprawki Webu znalezione przez J3 i J8: nazwy pól `created_at`/`completed_at` (lista pokazywała „Zlecone: 1 stycznia 1”), rodzaj pakietu pisany małymi literami w publicznym JSON (pakiet czatu pokazywał „500 zadań”), cofnięta próba `forceLoad`.
- J8 nie zakłada, że `/v1` jest pod adresem strony: bierze adres z potwierdzenia zakupu i woła Booking wprost.
- Zużycie czatu w panelu to tysiące tokenów dzielone całkowicie, więc małe użycie to „wykorzystano 0 z 500 tys. tokenów”. To decyzja o prezentacji, nie błąd.
- Znany wyścig na InMemory (profil `fake`): `AutoCompleteJob` i równoległa reklamacja pakietu o wyczerpanych jednostkach; na PostgreSQL chroni blokada wiersza.

## Zaostrzenie `AgentsFeature` (D-03)

Pytanie z zadania: czy wdrożony host bez `Agents:Provider` ma nie startować? Sprawdzone przez zmianę `AgentsFeature` tak, by brak ustawienia poza profilem `fake` zatrzymywał start (usunięty warunek `!string.IsNullOrWhiteSpace(provider)` przy filtrze startu). Wynik: `dotnet test --filter "tier!=integration"` miał **26 porażek** w `Gabinety.Booking.Tests` (m.in. `PublicEdgeTests`, `RouteAuthTests`, `ClockApiTests`, `MockPaymentsFailClosedTests`, `SmtpWiringTests`, `AgentTaskJobTests.A_deployed_host_without_a_platform_starts...`), bo testy profilu wdrożonego uruchamiają host bez platformy. Zmianę **wycofano**. Zamiast niej konfiguracja wdrożenia wymusza ustawienia: `Agents__Provider = "Http"` i adres platformy w `booking.fly.toml`, wymagany sekret `AGENTS_UPSTREAM_API_KEY` w `secrets.map` i wiersze `required` w `config-keys.map`. Host bez tych ustawień nie zostanie wdrożony (wdrożenie odmawia placeholdera i brakującego sekretu), a przypadkowy start bez nich daje błąd przy pierwszym użyciu. Gdyby właściciel chciał twardego startu w kodzie, trzeba najpierw dać testom profilu wdrożonego atrapę platformy.

## Otwarte decyzje właściciela

Poniższe rekomendacje to propozycje wykonawcy, nie rozstrzygnięcia.

### 1. Czy `/v1` ma być pod adresem strony?

Dziś `/v1` odpowiada Booking (publiczny adres aplikacji Booking, `PublicEdge`), a strona nie ma trasy `/v1`. **Ale** sekcja „Jak się połączyć” (profil agenta, potwierdzenie zakupu) poza profilem z płatnościami próbnymi podaje klientowi `Site:BaseUrl` + `/v1`, czyli adres strony. Na wdrożeniu z prawdziwymi płatnościami przykład `curl` z tej sekcji wskazywałby więc adres, który nie obsługuje `/v1`. To jedyna rozbieżność w kodzie, którą ta decyzja usuwa.

| Opcja | Co trzeba zrobić | Koszt |
|---|---|---|
| A. `/v1` zostaje na adresie Booking | dodać ustawienie adresu API w Web (np. `Site:ApiBaseUrl`) i używać go w `WebOptions.ApiBaseUrl`; podać klientom adres Bookinga lub własną domenę wskazującą na niego | najmniej pracy; klient widzi inny host niż strona |
| B. `/v1` pod adresem strony | przekazywanie `/v1` z Web do Booking (w tym strumień SSE bez buforowania, nagłówek `Authorization`, limit per klucz, który dziś zależy od klucza, nie od adresu) | Web staje się częścią ścieżki API; ryzyko dla strumieni i limitów |
| C. Osobna usługa API (E-05) | wydzielić `/v1` z Booking | najwięcej pracy; sens, gdy ruch API zacznie obciążać Booking |

Rekomendacja: A (z własną domeną typu `api.<domena>`), bo B zmienia profil ruchu strony, a C jest przedwczesne. Do czasu decyzji przykład `curl` na produkcji jest mylący.

### 2. Co robić, gdy ostatnie zadanie kończy się `failed` po auto-zakończeniu pakietu?

`AutoCompleteJob` przenosi pakiet `Tasks` w `Completed`, gdy `UnitsUsed >= UnitsTotal`, a wypłata dostawcy zostaje wyznaczona (`EndUtc + 72 h`, patrz wyżej). Jeśli ostatnie zadanie potem zakończy się `failed` (trzy nieudane starty), jednostka wraca, ale pakiet już jest `Completed`, więc klient ma wolną jednostkę, której nie może użyć (`POST /v1/tasks` szuka pakietów `Confirmed`).

| Opcja | Skutek |
|---|---|
| A. Nic nie robić | klient musi zgłosić reklamację (pakiet `Completed` do `EndUtc + 72 h`); najprostsze, ale to ręczna praca administratora |
| B. Zwrot jednostki cofa pakiet do `Confirmed` | wymaga nowego przejścia w `BookingStateMachine` i anulowania wyznaczonej wypłaty; więcej kodu, najlepsze dla klienta |
| C. `AutoCompleteJob` nie kończy pakietu, dopóki są zadania `queued` lub `running` | zapobiega problemowi u źródła; pakiet kończy się trochę później |

Rekomendacja: C. Zmienia jeden warunek zadania w tle, nie dodaje przejść stanu i nie dotyka wypłat. Żadna ścieżka e2e dziś tego nie wywołuje (atrapa platformy nie zawodzi), więc test jeszcze nie istnieje.

### 3. Rozliczanie rozłączonego strumienia czatu

Dziś nic nie jest naliczane, gdy klient rozłączy się przed końcem strumienia: `IAgentUpstream.StreamAsync` zwraca liczbę tokenów dopiero po całym strumieniu, a w `finally` rozlicza się to, co zwrócił (0). Platforma mogła wykonać pracę, więc klient dostaje ją za darmo (dotyczy rozłączenia przed fragmentem `usage`).

| Opcja | Skutek |
|---|---|
| A. Zostawić | prosto; ryzyko nadużycia: rozłączanie przed fragmentem `usage` daje darmowy czat |
| B. Podsłuchiwać fragment `usage` w strumieniu wyjściowym i rozliczać ostatnią znaną wartość | zmiana kontraktu `IAgentUpstream` (B-03); `usage` przychodzi na końcu, więc rozłączenie w środku nadal nic nie da |
| C. Rozliczyć oszacowanie (np. liczba znaków przesłanych klientowi / 4 + wejście) po rozłączeniu | zawsze coś naliczy; to szacunek, nie dane dostawcy |
| D. Wymagać od platformy kontynuacji zadania po rozłączeniu i odczytać `usage` osobnym zapytaniem | zależy od możliwości platformy, której kontraktu nie znamy |

Rekomendacja: C z oznaczeniem w `UsageRecord`, że wartość jest szacunkiem, albo A, jeśli ryzyko nadużycia jest małe (pakiet jest opłacony z góry i ważny 30 dni). Decyzja zależy od tego, co platforma agentów zwraca przy zerwanym połączeniu.

### 4. Odświeżanie listy zadań

`BookingDetail.razor` ustawia `<meta http-equiv="refresh" content="5">`, gdy są zadania `queued` lub `running`, i pokazuje „Zadanie jest w toku. Strona odświeża się sama.” Po zleceniu zadania następuje nawigacja rozszerzona (enhanced navigation), po której meta refresh nie działa, tak samo dla adresu z fragmentem `#zadania`. Napis nie jest więc prawdą, a zadanie zostaje „W kolejce”, dopóki klient nie kliknie „Odśwież listę”. Dodatkowo axe zgłasza `meta-refresh` jako błąd krytyczny (WCAG 2.2.1), gdy strona jest w stanie „w toku”; testy J3, J5 i J6 pilnują, żeby axe oglądał stronę po odpowiedzi agenta (świadome ominięcie).

| Opcja | Skutek |
|---|---|
| A. Zostawić | błąd dostępności zgłaszany przez axe przy stanie „w toku”, napis niezgodny z działaniem |
| B. Usunąć napis „Strona odświeża się sama” i meta refresh, zostawić „Odśwież listę” | prawdziwy komunikat, brak błędu axe; klient odświeża ręcznie |
| C. Skrypt (np. mały `enhance.js` odświeżający listę co kilka sekund, wyłączalny przez użytkownika) | zgodne z WCAG, jeśli da się to zatrzymać; strona działa bez skryptu jak dziś |
| D. Strona interaktywna (Blazor circuit) odpytuje stan | wymaga trybu interaktywnego na tej stronie; drożej w pamięci (`docs/operations/PERFORMANCE.md`) |

Rekomendacja: B teraz (najmniejsza zmiana, napis przestaje kłamać, znika błąd axe), C później, jeśli klienci będą czekać na wyniki dłużej niż kilkadziesiąt sekund.

## Skutki

- Kod i dokumenty opisują agentów AI; stare nazwy w kodzie to dług opisany w etapie E planu.
- Otwarte decyzje 1 do 4 blokują: decyzja 1 podanie klientom adresu API na produkcji, decyzja 2 pewność rozliczeń zadań, decyzja 3 uczciwość rozliczenia czatu, decyzja 4 poprawność komunikatu w panelu.
- Przed prawdziwymi płatnościami trzeba wykonać zmiany z `docs/operations/PAYMENTS-AGENTS.md` (E-03; wymaga nowej decyzji zastępującej propozycję 0011).
- Połączenie z prawdziwą platformą agentów nie zostało sprawdzone: `HttpAgentUpstream` ma testy, ale nie działał z żadną prawdziwą platformą, a kroki GitHub Copilot BYOK są niezweryfikowane (`docs/operations/COPILOT-BYOK.md`).
