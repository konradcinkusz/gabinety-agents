# GitHub Copilot BYOK: podłączenie agenta jako dostawcy zgodnego z OpenAI

> **Status: kroki po stronie GitHub Copilot są NIEZWERYFIKOWANE.** Ten dokument opisuje wyłącznie to, co wiadomo z kodu tego repozytorium: jak wygląda nasze API. Sesja, która go pisała, nie miała dostępu do dokumentacji GitHub Copilot, więc **nie zawiera** instrukcji, gdzie w Copilocie dodać własnego dostawcę ani jakie pola wypełnić. Nie publikuj go klientom, dopóki nie zostanie wypełniona sekcja [Do zweryfikowania przed publikacją](#do-zweryfikowania-przed-publikacją).

## Co wiadomo z kodu

Agent z pakietem `Chat` jest dostępny jako zwykłe API zgodne z OpenAI Chat Completions (pełny opis: `docs/operations/AGENT-API.md`).

| Co | Wartość |
|---|---|
| Adres bazowy | `<adres Booking>/v1`. Profil `fake`: `http://localhost:5102/v1`. Na produkcji adres zależy od otwartej decyzji właściciela, czy `/v1` jest pod adresem strony (`docs/adr-proposals/0013-agent-marketplace.md`); strona „Jak się połączyć” na profilu agenta pokazuje adres, który dziś podaje klientowi. |
| Uwierzytelnianie | nagłówek `Authorization: Bearer gab_...` (klucz API z `/moje/klucze`; pełny klucz widać tylko raz) |
| Nazwa modelu | **slug agenta**, np. `demo-bartosz-testowy` (lista: `GET /v1/models`) |
| Lista modeli | `GET /v1/models` zwraca tylko agentów, u których konto ma użyteczny pakiet `Chat` |
| Czat | `POST /v1/chat/completions`, zwykły i strumień SSE (`"stream": true`) |

Kształt odpowiedzi `GET /v1/models`:

```json
{"object":"list","data":[{"id":"demo-bartosz-testowy","object":"model","owned_by":"gabinety"}]}
```

Wymagania, które nasze API spełnia (z kodu i testów):

- **`tools`, `tool_choice` i pozostałe pola żądania są przekazywane dostawcy agentów bez zmian.** Czy dany agent faktycznie wykonuje wywołania narzędzi, zależy od zewnętrznej platformy agentów, nie od tej aplikacji.
- **Strumień SSE z `usage`.** Przy `"stream": true` serwer wymusza `stream_options.include_usage = true` u dostawcy, a ostatni fragment przed `data: [DONE]` niesie `usage` (bez `choices`). Strumień kończy `data: [DONE]`.
- Błędy mają kształt OpenAI: `{"error":{"message","type","code"}}`; `401 invalid_api_key`, `404 model_not_found` (brak pakietu u tego agenta), `429 insufficient_quota` (pakiet wyczerpany), `429 rate_limit_exceeded` (limit klucza, z `Retry-After`), `502 upstream_error`.
- Rozliczenie: tokeny z `usage.total_tokens` odpowiedzi, po fakcie (ostatnie żądanie może przekroczyć pulę). **Rozłączony strumień nie jest dziś rozliczany.**

Co dziś **nie** jest zaimplementowane po naszej stronie (sprawdzone w kodzie): `GET /v1/models/{id}`, punkty `embeddings`, `completions`, `responses`; `/v1/models` pokazuje tylko slug (bez limitów kontekstu, możliwości czy opisu). Jeśli Copilot tego wymaga, trzeba to dopisać.

Przykład wywołania sprawdzający całość bez Copilota:

```bash
curl -s http://localhost:5102/v1/models -H "Authorization: Bearer $KEY"
curl -sN http://localhost:5102/v1/chat/completions -H "Authorization: Bearer $KEY" -H "Content-Type: application/json" \
  -d '{"model":"demo-bartosz-testowy","stream":true,"messages":[{"role":"user","content":"test"}]}'
```

## Do zweryfikowania przed publikacją

Każdy punkt trzeba sprawdzić w aktualnej dokumentacji GitHub Copilot (albo w samym produkcie) i wpisać odpowiedź ze źródłem oraz datą sprawdzenia. Do tego czasu nie wolno pisać kroków „kliknij ...” ani nazw pól.

1. Czy Copilot BYOK akceptuje dostawcę zgodnego z OpenAI pod **własnym adresem** (nie tylko wymienionych dostawców)? W których edytorach i wersjach (VS Code, JetBrains, inne, github.com)?
2. Jakie pola konfiguracji trzeba podać (adres bazowy, klucz, nazwa modelu, identyfikator, limity kontekstu, obsługa narzędzi, obrazów) i gdzie w interfejsie się je dodaje? Czy adres ma kończyć się na `/v1`, czy Copilot sam dopisuje `/chat/completions`?
3. Czy Copilot wymaga innych punktów niż `GET /v1/models` i `POST /v1/chat/completions` (np. `GET /v1/models/{id}`, pola `capabilities` czy `context_length` w liście modeli)?
4. Czy **tryb agenta** wymaga obsługi `tools` po stronie modelu? Czy czat zwykły i tryb agenta mają różne wymagania wobec własnego dostawcy? Jak wykrywane jest wsparcie dla narzędzi?
5. Czy Copilot wymaga strumieniowania (`stream: true`), czy używa `stream_options.include_usage`, jak liczy tokeny i czy jego liczba może się rozjechać z `usage` z naszej odpowiedzi?
6. Ograniczenia planów: które plany Copilota (Free, Pro, Business, Enterprise) pozwalają na BYOK, czy administrator organizacji musi go włączyć, czy dotyczy to także github.com i CLI?
7. Czy Copilot przesyła ruch przez serwery GitHub (wtedy nasz adres musi być publiczny i dostępny z internetu), czy wywołuje dostawcę bezpośrednio z maszyny użytkownika? Od tego zależy, czy wystarczy adres `localhost` w profilu `fake`.
8. Czy Copilot wysyła dodatkowe nagłówki lub pola żądania, które nasze API odrzuca (np. nieznane pola, `store`, `parallel_tool_calls`)? U nas każde pole jest przekazywane dostawcy agentów, ale ciało nie może przekroczyć 2 MB.
9. Jakie limity żądań stosuje Copilot i czy mieszczą się w limicie klucza (domyślnie 60 żądań na minutę na klucz)?
10. Warunki użycia: czy sprzedaż dostępu do agentów jako własnego dostawcy w Copilocie jest zgodna z regulaminem GitHub?

## Po weryfikacji

Gdy punkty wyżej mają odpowiedzi: dopisz sekcję „Kroki w Copilocie” z cytatem lub odnośnikiem do dokumentacji i datą sprawdzenia, zmień znacznik statusu w nagłówku i zaktualizuj sekcję „Jak się połączyć” na profilu agenta (`src/Gabinety.Web`) oraz test J1/J8, jeśli zmieni się adres lub nazwa pola.
