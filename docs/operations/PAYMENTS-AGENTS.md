# Zmiany potrzebne przed prawdziwymi płatnościami (agenci AI)

Krótki opis, nie plan wykonawczy. To jest zadanie E-03 z `docs/agentic/PLAN.md` i wymaga nowej decyzji ADR, która zastąpi propozycję 0011 (`docs/adr-proposals/0011-payment-provider-stripe-connect.md`). Dziś płatności działają tylko jako płatność próbna w profilu `fake`; adapter Stripe istnieje, ale **żadne żądanie nie dotarło do Stripe** (`docs/operations/PAYMENTS-GO-LIVE.md`).

## Co się zmienia w modelu

Propozycja 0011 zakłada marketplace: klient płaci, a platforma po terminie przelewa pieniądze **dostawcy** (Stripe Connect, osobne obciążenia i przelewy, prowizja platformy, konta połączone dostawców). W wersji agentowej **sprzedawcą jest platforma**: klient kupuje dostęp do agentów od platformy, a dostawca agenta jest jej partnerem technicznym, nie stroną transakcji z klientem. Wynika z tego, że wypłaty dla dostawców i konta połączone przestają być potrzebne.

## Do usunięcia (E-03)

Nazwy z kodu `src/Gabinety.Booking`, które dziś obsługują wypłaty i konta połączone:

- wypłaty: encja i tabela `Payout` (`Features/Payouts`), `PayoutJob` (`Jobs/Payout`), pole terminu wypłaty w zakupie (`PayoutDueAt`, dziś `EndUtc + 72 h`), wpisy księgi `PractitionerPayout`;
- prowizja: `FeeHook`, `PairKey` (reguła „jedna prowizja na parę klient i dostawca”), wpisy `PlatformFee`, ustawienie `Billing__FirstConsultationFeeBps`, `Fees` w `Gabinety.Billing`;
- konta połączone: `PaymentAccount`, `PaymentAccountFeature`, `IConnectedAccountProvider` i jego implementacje (Stripe, Mock), strona rejestracji konta wypłat w panelu dostawcy (`PaymentsLanding`, `/practitioner/payments`), warunek zakupu `practitioner_payments_not_ready` (w `CreateAccessFeature`, w kontraktach i w tekstach strony) oraz stan konta płatności w profilu dostawcy;
- w adapterze Stripe: obciążenia na koncie dostawcy i przelewy (`transfers`) zamiast zwykłego obciążenia konta platformy; zdarzenia i uprawnienia klucza ograniczonego (Transfers, Accounts, Account Links) przestają być potrzebne, zostają płatność, zwrot i webhook.

Do przemyślenia razem z usunięciem (nie zakładam rozwiązania):

- **Księga i raport administratora** (`/admin/ksiega`, eksport CSV): wpisy `ClientCharge` i `Refund` zostają, `PlatformFee` i `PractitionerPayout` znikają; J6 sprawdza dziś arytmetykę z wypłatą (cena 25000, zwrot 10000, prowizja 2250, wypłata 12750) i trzeba ją przepisać.
- **Reklamacja i zwrot częściowy**: dziś administrator rozstrzyga reklamację zwrotem; po zmianie zwrot jest wyłącznie zwrotem środków klientowi, bez skutków dla wypłaty.
- **Rozliczenie z dostawcami agentów**: poza tą aplikacją (umowa z platformą agentów); aplikacja nie liczy już należności dostawców.
- **Istniejące dane**: migracja usuwająca tabele i kolumny wypłat (nowa migracja EF Core, nie edycja starych) oraz sprawdzenie, że żaden zakup nie ma wyznaczonej, niewypłaconej kwoty.
- **Stan `Resolved`**: `PayoutJob` wypłaca rozstrzygnięty zakup od chwili rozstrzygnięcia (`ResolvedAt`); po usunięciu wypłat rozstrzygnięcia `release`, `refund_full` i `refund_partial` (`AdminDisputesFeature`) zostają, ale tylko jako decyzja o zwrocie.

## Do ustalenia przez właściciela (poza zakresem kodu)

Poniższe to pytania, nie rozstrzygnięcia i nie porady prawne; wykonawca ich nie weryfikował.

- Podmiot sprzedający (spółka właściciela), faktury i podatek od sprzedaży dostępu cyfrowego, regulamin i polityka prywatności (`<!-- DO WERYFIKACJI PRAWNEJ -->` w `Content/Legal/*.md` pozostaje do czasu przeglądu prawnika).
- Warunki umowy z platformą agentów i cennik jej wywołań (koszt tokenów i zadań wobec ceny pakietów).
- Zasady reklamacji wobec klienta jako konsumenta (okno `EndUtc + 72 h` jest decyzją produktową, nie prawną).
- Konto Stripe i tryb testowy według `PAYMENTS-GO-LIVE.md`; kroki dotyczące Connect z tego dokumentu zostaną niepotrzebne.

## Co zostaje bez zmian

`IPaymentProvider` (płatność, zwrot, webhook), płatność próbna w profilu `fake`, stany pakietu i ich przejścia, reguła „pełny zwrot tylko przed pierwszym użyciem”, plakietka „Agent AI”, klucze API i API `/v1`.
