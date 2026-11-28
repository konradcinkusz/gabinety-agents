# Mapa zmian nazw w kodzie (E-06)

Kod mówi językiem produktu. Zmieniają się wyłącznie **identyfikatory C#** (typy, składowe, parametry, zmienne, pliki, składniki Razor).
Nazwy na drucie, w bazie i w konfiguracji **nie zmieniają się** (patrz „Co zostaje bez zmian").

## 1. Reguły słownikowe (po członach nazwy w PascalCase / camelCase)

| Stara część | Nowa część | Liczba mnoga |
|---|---|---|
| `Practitioner` / `practitioner` | `Agent` / `agent` | `Agents` |
| `Booking` / `booking` | `Access` / `access` | `Accesses` |
| `Offering` / `offering` | `Package` / `package` | `Packages` |

Przykłady: `PractitionerCard` → `AgentCard`, `PractitionerStatus` → `AgentStatus`, `BookingStatus` → `AccessStatus`, `CreateBookingFeature` → `CreateAccessFeature`, `BookingDbContext` → `AccessDbContext`, `OfferingKind` → `PackageKind`, `MyBookings` → `MyAccesses`.

## 2. Co zostaje bez zmian (i dlaczego)

| Element | Powód |
|---|---|
| Nazwy projektów, `.csproj`, przestrzeń nazw `Gabinety.Booking` (i `Gabinety_Booking` z AppHost), aliasy `extern alias BookingApp` | twarde ograniczenie 3: projekty, usługi, Fly, obrazy, CI, AppHost, deploy/ |
| Segment przestrzeni nazw dokładnie `Booking` (np. `Gabinety.Contracts.Booking`, `Gabinety.Web.Components.Pages.Booking`) | oznacza obszar usługi Booking, nie zakup pakietu |
| Pola JSON, wartości enumów w API, trasy `/api/v1/...`, kody błędów, nagłówki | format na drucie |
| Składowe enumów, których nazwa zawiera stare słowo (`NotificationKind.BookingConfirmed`, `BookingStatus.CancelledByPractitioner`, `BookingTrigger.PractitionerCancels`, `RefundReason.PractitionerCancelled`, `MoneyEvent.PractitionerCancelled`, `DisputeKind.PractitionerNoShow`, `LedgerKind.PractitionerPayout`, `ChecklistKey.Offering`) | wartości zapisywane w bazie lub zwracane jako tekst |
| Nazwy tabel i kolumn, `ToTable` / `HasColumnName`, migracje (klasy, pliki, `[Migration]`) | schemat bazy bez zmian, brak nowej migracji |
| Nazwy elementów typów anonimowych (`new { practitionerCancelCount = ... }`) | to pola JSON wysyłane do usług |
| Literały tekstowe (teksty PL, adresy, klucze konfiguracji, szablony logów) | nie ten etap |
| Dane historyczne: `docs/ui`, pomiary, `PARITY.md`, stare ADR, `PLAN.md`, `tests/e2e` | zostają bez zmian |
| Komentarze prozą (nie `cref`) | opis, nie kod |
| Parametry tras `@page` w Razor (`BookingId`, `OfferingId` w szablonach `/moje/dostepy/{BookingId}`, `/dostep/{OfferingId}` itd.) | Blazor wiąże parametr z szablonu trasy po nazwie właściwości; zmiana nazwy zmieniłaby szablon i `docs/operations/ROUTE-AUTH.md` (test RouteAuditTests) |
| Parametry handlerów minimal API związane z trasą lub zapytaniem (`practitionerId` w `/api/v1/practitioners/{practitionerId:guid}/reviews`, `bookingId` w `me/bookings/{bookingId:guid}/tasks`, `minBookings` w `pair-counts`) | nazwa jest w adresie; dodano `[FromRoute(Name = ...)]` / `[FromQuery(Name = ...)]`, więc zmienna ma nową nazwę, a adres starą |
| `extern alias BookingApp` w testach integracyjnych | alias projektu z `.csproj` (projekty bez zmian) |
| `src/Gabinety.AppHost` | twarde ograniczenie 3 (test `AppHostWiringTests` czyta jego tekst) |
| Nazwy tabel i kolumn w SQL i w `FromSql` (`bookings`, `practitioner_id`) | schemat bez zmian |

Snapshoty modeli migracji: zmienia się tylko nazwa klasy snapshotu i `typeof(...DbContext)` w `[DbContext]`; nazwy encji w snapshotach (łańcuchy tekstowe) zostają, bo tabele i identyfikatory migracji się nie zmieniają.

## 3. Kolizje z istniejącymi nazwami (rozwiązane ręcznie)

| Stara nazwa | Naiwna nowa nazwa | Powód | Nazwa użyta |
|---|---|---|---|
| `DirectoryPractitioner` (Booking) | `DirectoryAgent` | taki typ już istnieje (agent z `Slug`, E-04) | `DirectoryAgentState` |
| `InternalPractitionerDto` (Directory) | `InternalAgentDto` | taki typ już istnieje (E-04) | `InternalAgentStateDto` |
| lokalna `practitioner` w `PaymentAccountFeature` | `agent` | `agent` to parametr zapytania `?agent=` tej samej metody | `agentId` |

## 4. Tabela mapowania identyfikatorów

Kolejność zmian w historii: Contracts i Billing, Directory, Booking, Reputation i inne usługi, Web, tools, testy, dokumentacja.
Nazwa trafia do commita obszaru, w którym jest zadeklarowana jako pierwszym; zmienia się wtedy wszędzie w repozytorium naraz (kompilator pilnuje spójności).


### 1. Contracts i Billing

| Stara nazwa | Nowa nazwa |
|---|---|
| `Booking` | `Access` |
| `BookingId` | `AccessId` |
| `BookingStateMachine` | `AccessStateMachine` |
| `BookingStatus` | `AccessStatus` |
| `BookingTrigger` | `AccessTrigger` |
| `BookingView` | `AccessView` |
| `Bookings` | `Accesses` |
| `CreateBookingRequest` | `CreateAccessRequest` |
| `CreateBookingResponse` | `CreateAccessResponse` |
| `CreatePractitionerRequest` | `CreateAgentRequest` |
| `Offering` | `Package` |
| `OfferingId` | `PackageId` |
| `OfferingInactive` | `PackageInactive` |
| `OfferingKind` | `PackageKind` |
| `OfferingPrivate` | `PackagePrivate` |
| `OfferingRequest` | `PackageRequest` |
| `OfferingTitle` | `PackageTitle` |
| `Offerings` | `Packages` |
| `OwnOffering` | `OwnPackage` |
| `Practitioner` | `Agent` |
| `PractitionerCard` | `AgentCard` |
| `PractitionerCount` | `AgentCount` |
| `PractitionerExists` | `AgentExists` |
| `PractitionerId` | `AgentId` |
| `PractitionerNotBookable` | `AgentNotBookable` |
| `PractitionerPaymentsNotReady` | `AgentPaymentsNotReady` |
| `PractitionerPrivate` | `AgentPrivate` |
| `PractitionerProfile` | `AgentProfile` |
| `PractitionerRef` | `AgentRef` |
| `PractitionerShare` | `AgentShare` |
| `PractitionerStatus` | `AgentStatus` |
| `PractitionerSummary` | `AgentSummary` |
| `Practitioners` | `Agents` |
| `SitemapPractitioner` | `SitemapAgent` |
| `TooManyOfferings` | `TooManyPackages` |
| `UpdatePractitionerRequest` | `UpdateAgentRequest` |

### 2. Directory

| Stara nazwa | Nowa nazwa |
|---|---|
| `ActiveOfferingCountAsync` | `ActivePackageCountAsync` |
| `AdminPractitionerDetail` | `AdminAgentDetail` |
| `AdminPractitionerPage` | `AdminAgentPage` |
| `AdminPractitionerRow` | `AdminAgentRow` |
| `BookingClient` | `AccessClient` |
| `BookingClientFeature` | `AccessClientFeature` |
| `BookingStatsPush` | `AccessStatsPush` |
| `BookingUnavailableException` | `AccessUnavailableException` |
| `CancelFutureBookingsAsync` | `CancelFutureAccessesAsync` |
| `GetOffering` | `GetPackage` |
| `IBookingClient` | `IAccessClient` |
| `InternalOfferingDto` | `InternalPackageDto` |
| `InternalPractitionerDto` | `InternalAgentStateDto` |
| `InternalPractitionerNameDto` | `InternalAgentNameDto` |
| `NoOfferingsYet` | `NoPackagesYet` |
| `OfferingConfiguration` | `PackageConfiguration` |
| `OfferingFacts` | `PackageFacts` |
| `OfferingFilter` | `PackageFilter` |
| `OfferingsFeature` | `PackagesFeature` |
| `PractitionerCancelCount` | `AgentCancelCount` |
| `PractitionerConfiguration` | `AgentConfiguration` |
| `PractitionerSlug` | `AgentSlug` |
| `PractitionerStateMachine` | `AgentStateMachine` |
| `PractitionerTrigger` | `AgentTrigger` |
| `PractitionerUserId` | `AgentUserId` |
| `booking` | `access` |
| `byPractitioner` | `byAgent` |
| `offering` | `package` |
| `offerings` | `packages` |
| `practitionerId` | `agentId` |
| `practitioners` | `agents` |

### 3. Booking

| Stara nazwa | Nowa nazwa |
|---|---|
| `AsPractitioner` | `AsAgent` |
| `BookingConfiguration` | `AccessConfiguration` |
| `BookingDbContext` | `AccessDbContext` |
| `BookingDbContextFactory` | `AccessDbContextFactory` |
| `BookingDbContextModelSnapshot` | `AccessDbContextModelSnapshot` |
| `BookingEntity` | `AccessEntity` |
| `BookingFlags` | `AccessFlags` |
| `BookingPermissions` | `AccessPermissions` |
| `BookingStats` | `AccessStats` |
| `BookingTransitionContext` | `AccessTransitionContext` |
| `BookingTransitionResult` | `AccessTransitionResult` |
| `BookingTransitions` | `AccessTransitions` |
| `BookingViewFeature` | `AccessViewFeature` |
| `CancelFutureBookingsFeature` | `CancelFutureAccessesFeature` |
| `CancelFutureBookingsResult` | `CancelFutureAccessesResult` |
| `CreateBookingFeature` | `CreateAccessFeature` |
| `CreateForBookingAsync` | `CreateForAccessAsync` |
| `DefaultMinBookings` | `DefaultMinAccesses` |
| `DirectoryOffering` | `DirectoryPackage` |
| `DirectoryPractitioner` | `DirectoryAgentState` |
| `DirectoryPractitionerName` | `DirectoryAgentName` |
| `FutureBookingCanceller` | `FutureAccessCanceller` |
| `GetOfferingAsync` | `GetPackageAsync` |
| `GetPractitionerAgentsAsync` | `GetAgentAgentsAsync` |
| `GetPractitionerByUserAsync` | `GetAgentByUserAsync` |
| `GetPractitionerContactAsync` | `GetAgentContactAsync` |
| `GetPractitionerNameAsync` | `GetAgentNameAsync` |
| `IBookingTransitionHook` | `IAccessTransitionHook` |
| `IsPractitioner` | `IsAgent` |
| `ListForBookingAsync` | `ListForAccessAsync` |
| `MaxMinBookings` | `MaxMinAccesses` |
| `MinMinBookings` | `MinMinAccesses` |
| `MyBookingsFeature` | `MyAccessesFeature` |
| `MyBookingsPage` | `MyAccessesPage` |
| `PractitionerCancellation` | `AgentCancellation` |
| `PractitionerDisplayName` | `AgentDisplayName` |
| `PractitionerFallback` | `AgentFallback` |
| `PractitionerResolver` | `AgentResolver` |
| `RequiresPractitionerAccount` | `RequiresAgentAccount` |
| `afterPractitioner` | `afterAgent` |
| `bookingId` | `accessId` |
| `bookings` | `accesses` |
| `forPractitioner` | `forAgent` |
| `isPractitioner` | `isAgent` |
| `minBookings` | `minAccesses` |
| `offeringId` | `packageId` |
| `practitioner` | `agent` |
| `practitionerSlug` | `agentSlug` |

### 4. Reputation, Assistant, Ranking, Llm, ServiceDefaults

| Stara nazwa | Nowa nazwa |
|---|---|
| `BookingUnavailableForAnalyticsException` | `AccessUnavailableForAnalyticsException` |
| `BookingUrl` | `AccessUrl` |
| `MinBookings` | `MinAccesses` |
| `bookingDb` | `accessDb` |

### 5. Web

| Stara nazwa | Nowa nazwa |
|---|---|
| `ActivateOfferingAsync` | `ActivatePackageAsync` |
| `AdminBookingAsync` | `AdminAccessAsync` |
| `AdminBookingDto` | `AdminAccessDto` |
| `AdminPractitionerActionAsync` | `AdminAgentActionAsync` |
| `AdminPractitionerAsync` | `AdminAgentAsync` |
| `AdminPractitionerDetailDto` | `AdminAgentDetailDto` |
| `AdminPractitionerRowDto` | `AdminAgentRowDto` |
| `AdminPractitionersAsync` | `AdminAgentsAsync` |
| `AllBookings` | `AllAccesses` |
| `AllPractitioners` | `AllAgents` |
| `BecomePractitioner` | `BecomeAgent` |
| `BookOffering` | `BookPackage` |
| `BookingAttempt` | `AccessAttempt` |
| `BookingBadge` | `AccessBadge` |
| `BookingContext` | `AccessContext` |
| `BookingDetailDto` | `AccessDetailDto` |
| `BookingFormModel` | `AccessFormModel` |
| `BookingFormProblems` | `AccessFormProblems` |
| `BookingFormat` | `AccessFormat` |
| `BookingSteps` | `AccessSteps` |
| `BookingUnreadable` | `AccessUnreadable` |
| `BookingViewDto` | `AccessViewDto` |
| `BookingsText` | `AccessesText` |
| `ColBooking` | `ColAccess` |
| `ColBookings` | `ColAccesses` |
| `ColPractitioner` | `ColAgent` |
| `CreateBookingRequestDto` | `CreateAccessRequestDto` |
| `CreateBookingResponseDto` | `CreateAccessResponseDto` |
| `CreatePractitionerBody` | `CreateAgentBody` |
| `DeactivateOfferingAsync` | `DeactivatePackageAsync` |
| `EmptyPractitioner` | `EmptyAgent` |
| `FindPractitioner` | `FindAgent` |
| `ForBooking` | `ForAccess` |
| `ForPractitionersBody` | `ForAgentsBody` |
| `ForPractitionersLink` | `ForAgentsLink` |
| `ForPractitionersTitle` | `ForAgentsTitle` |
| `KindPractitionerPayout` | `KindAgentPayout` |
| `MyBookings` | `MyAccesses` |
| `MyBookingsAsync` | `MyAccessesAsync` |
| `MyBookingsSchedule` | `MyAccessesSchedule` |
| `NoOfferings` | `NoPackages` |
| `NotPractitioner` | `NotAgent` |
| `NotPractitionerTitle` | `NotAgentTitle` |
| `OfferingBody` | `PackageBody` |
| `OfferingDto` | `PackageDto` |
| `OfferingPrivateDto` | `PackagePrivateDto` |
| `OfferingsAsync` | `PackagesAsync` |
| `OfferingsDto` | `PackagesDto` |
| `OfferingsTitle` | `PackagesTitle` |
| `PartyPractitioner` | `PartyAgent` |
| `PlPractitioner` | `PlAgent` |
| `PractitionerAction` | `AgentAction` |
| `PractitionerArea` | `AgentArea` |
| `PractitionerCardDto` | `AgentCardDto` |
| `PractitionerClient` | `AgentClient` |
| `PractitionerLayout` | `AgentLayout` |
| `PractitionerMany` | `AgentMany` |
| `PractitionerName` | `AgentName` |
| `PractitionerNames` | `AgentNames` |
| `PractitionerOne` | `AgentOne` |
| `PractitionerOutcome` | `AgentOutcome` |
| `PractitionerPage` | `AgentPage` |
| `PractitionerPaths` | `AgentPaths` |
| `PractitionerPrivateDto` | `AgentPrivateDto` |
| `PractitionerProfileCheck` | `AgentProfileCheck` |
| `PractitionerProfileDto` | `AgentProfileDto` |
| `PractitionerQuery` | `AgentQuery` |
| `PractitionerRefDto` | `AgentRefDto` |
| `PractitionerRole` | `AgentRole` |
| `PractitionerRule` | `AgentRule` |
| `PractitionerSchedulingClient` | `AgentSchedulingClient` |
| `PractitionerStatusKind` | `AgentStatusKind` |
| `PractitionerSummariesDto` | `AgentSummariesDto` |
| `PractitionerSummaryDto` | `AgentSummaryDto` |
| `PractitionerTitle` | `AgentTitle` |
| `PractitionersAsync` | `AgentsAsync` |
| `PractitionersColumn` | `AgentsColumn` |
| `PractitionersFew` | `AgentsFew` |
| `PractitionersMany` | `AgentsMany` |
| `PractitionersOne` | `AgentsOne` |
| `SaveOfferingAsync` | `SavePackageAsync` |
| `SitemapPractitionerDto` | `SitemapAgentDto` |
| `SuspendedButBookings` | `SuspendedButAccesses` |
| `ToBooking` | `ToAccess` |
| `UpdatePractitionerBody` | `UpdateAgentBody` |
| `VisitorBecomePractitioner` | `VisitorBecomeAgent` |
| `_practitionerId` | `_agentId` |
| `_practitionerName` | `_agentName` |
| `practitionerLink` | `agentLink` |

### 6. tools (DemoSeeder, DevIdentity)

| Stara nazwa | Nowa nazwa |
|---|---|
| `BookingApi` | `AccessApi` |
| `BookingInfo` | `AccessInfo` |
| `BookingKey` | `AccessKey` |
| `BookingPlan` | `AccessPlan` |
| `BookingSeedStep` | `AccessSeedStep` |
| `BookingTarget` | `AccessTarget` |
| `CreateOfferingAsync` | `CreatePackageAsync` |
| `DemoBooking` | `DemoAccess` |
| `DemoOffering` | `DemoPackage` |
| `DemoPractitioner` | `DemoAgent` |
| `EnsureOfferingsAsync` | `EnsurePackagesAsync` |
| `MyOffering` | `MyPackage` |
| `MyPractitioner` | `MyAgent` |
| `PractitionersPerFactor` | `AgentsPerFactor` |
| `ScaledPractitioner` | `ScaledAgent` |
| `SeedPractitionerAsync` | `SeedAgentAsync` |
| `SeededPractitioner` | `SeededAgent` |
| `TotalPractitioners` | `TotalAgents` |
| `UpdateOfferingAsync` | `UpdatePackageAsync` |
| `bookingKey` | `accessKey` |

### 7. tests

| Stara nazwa | Nowa nazwa |
|---|---|
| `A_401_during_an_action_goes_to_sign_in_and_back_to_the_booking` | `A_401_during_an_action_goes_to_sign_in_and_back_to_the_access` |
| `A_Booking_outage_is_503_dependency_unavailable_writes_nothing_and_leaks_neither_url_nor_key` | `A_Access_outage_is_503_dependency_unavailable_writes_nothing_and_leaks_neither_url_nor_key` |
| `A_Directory_outage_costs_the_practitioner_one_attempt_and_the_message_is_delivered_after_recovery_while_the_client_is_not_delayed` | `A_Directory_outage_costs_the_agent_one_attempt_and_the_message_is_delivered_after_recovery_while_the_client_is_not_delayed` |
| `A_Disputed_booking_whose_payment_is_already_refunded_is_409_without_calling_the_provider` | `A_Disputed_access_whose_payment_is_already_refunded_is_409_without_calling_the_provider` |
| `A_bad_limit_or_cursor_is_400_and_Booking_is_not_asked` | `A_bad_limit_or_cursor_is_400_and_Access_is_not_asked` |
| `A_booking_Booking_says_is_not_reviewable_is_422_not_reviewable_and_nothing_is_written` | `A_access_Access_says_is_not_reviewable_is_422_not_reviewable_and_nothing_is_written` |
| `A_booking_created_after_moving_the_clock_uses_the_offset_time_for_created_at_and_the_hold` | `A_access_created_after_moving_the_clock_uses_the_offset_time_for_created_at_and_the_hold` |
| `A_booking_disputed_between_selection_and_payout_is_skipped` | `A_access_disputed_between_selection_and_payout_is_skipped` |
| `A_booking_is_refused_while_the_practitioner_has_no_account_and_nothing_is_created_or_sent_to_Stripe` | `A_access_is_refused_while_the_agent_has_no_account_and_nothing_is_created_or_sent_to_Stripe` |
| `A_booking_made_with_the_token_stores_Klient_and_an_initial_not_the_address` | `A_access_made_with_the_token_stores_Klient_and_an_initial_not_the_address` |
| `A_booking_moves_from_the_open_list_to_the_resolved_list_when_resolved` | `A_access_moves_from_the_open_list_to_the_resolved_list_when_resolved` |
| `A_booking_paid_between_selection_and_transition_is_left_Confirmed` | `A_access_paid_between_selection_and_transition_is_left_Confirmed` |
| `A_booking_request_does_not_need_startUtc` | `A_access_request_does_not_need_startUtc` |
| `A_booking_request_round_trips` | `A_access_request_round_trips` |
| `A_booking_state_has_a_label_and_a_kind` | `A_access_state_has_a_label_and_a_kind` |
| `A_booking_that_cannot_be_read_leaves_the_card_without_the_amount_and_says_so` | `A_access_that_cannot_be_read_leaves_the_card_without_the_amount_and_says_so` |
| `A_booking_that_cannot_be_read_says_refresh_the_page_with_the_link_that_does_it` | `A_access_that_cannot_be_read_says_refresh_the_page_with_the_link_that_does_it` |
| `A_booking_that_has_a_review_or_cannot_have_one_gets_a_card_with_the_way_back` | `A_access_that_has_a_review_or_cannot_have_one_gets_a_card_with_the_way_back` |
| `A_booking_that_is_no_longer_pending_says_so_and_confirmed_leads_to_the_booking_else_to_the_profile` | `A_access_that_is_no_longer_pending_says_so_and_confirmed_leads_to_the_access_else_to_the_profile` |
| `A_booking_view_with_its_status_as_the_wire_name` | `A_access_view_with_its_status_as_the_wire_name` |
| `A_booking_with_a_succeeded_or_partially_refunded_payment_counts_whatever_its_status` | `A_access_with_a_succeeded_or_partially_refunded_payment_counts_whatever_its_status` |
| `A_booking_without_a_client_address_is_given_up_for_the_client_but_the_practitioner_is_still_notified` | `A_access_without_a_client_address_is_given_up_for_the_client_but_the_agent_is_still_notified` |
| `A_booking_without_a_kept_payment_does_not_count` | `A_access_without_a_kept_payment_does_not_count` |
| `A_broken_cursor_is_400_and_an_unknown_practitioner_is_an_empty_page` | `A_broken_cursor_is_400_and_an_unknown_agent_is_an_empty_page` |
| `A_cancelled_booking_states_the_refund_and_offers_to_book_again_but_not_to_a_practitioner` | `A_cancelled_access_states_the_refund_and_offers_to_book_again_but_not_to_a_agent` |
| `A_card_shows_the_practitioner_with_the_hooks_the_journey_uses` | `A_card_shows_the_agent_with_the_hooks_the_journey_uses` |
| `A_category_is_indexable_from_three_listed_practitioners_not_from_two` | `A_category_is_indexable_from_three_listed_agents_not_from_two` |
| `A_category_with_fewer_than_three_practitioners_is_noindex_and_a_full_one_is_indexed` | `A_category_with_fewer_than_three_agents_is_noindex_and_a_full_one_is_indexed` |
| `A_client_gets_bookings_the_account_menu_and_sign_out_always_and_no_administration` | `A_client_gets_accesses_the_account_menu_and_sign_out_always_and_no_administration` |
| `A_client_who_is_no_practitioner_gets_404_and_so_does_an_unknown_review` | `A_client_who_is_no_agent_gets_404_and_so_does_an_unknown_review` |
| `A_completed_booking_is_not_paid_before_the_claim_window_has_closed` | `A_completed_access_is_not_paid_before_the_claim_window_has_closed` |
| `A_completed_booking_leads_with_the_review_and_offers_to_book_again` | `A_completed_access_leads_with_the_review_and_offers_to_book_again` |
| `A_confirmed_booking_says_so_in_the_exact_words_with_the_summary_the_name_the_state_and_the_ways_on` | `A_confirmed_access_says_so_in_the_exact_words_with_the_summary_the_name_the_state_and_the_ways_on` |
| `A_confirmed_paid_booking_shows_every_value_as_a_definition_the_journeys_can_find` | `A_confirmed_paid_access_shows_every_value_as_a_definition_the_journeys_can_find` |
| `A_different_client_or_a_different_practitioner_is_a_different_pair` | `A_different_client_or_a_different_agent_is_a_different_pair` |
| `A_different_client_with_the_same_key_gets_its_own_booking` | `A_different_client_with_the_same_key_gets_its_own_access` |
| `A_dispute_at_start_plus_60_h_on_a_Completed_booking_succeeds_and_reopens_the_money` | `A_dispute_at_start_plus_60_h_on_a_Completed_access_succeeds_and_reopens_the_money` |
| `A_dispute_card_shows_what_the_administrator_needs_to_decide_and_the_booking_id_as_text_the_J6_hook` | `A_dispute_card_shows_what_the_administrator_needs_to_decide_and_the_access_id_as_text_the_J6_hook` |
| `A_disputed_booking_is_never_completed_even_when_its_id_comes_from_a_stale_selection` | `A_disputed_access_is_never_completed_even_when_its_id_comes_from_a_stale_selection` |
| `A_due_completed_booking_is_paid_as_a_transfer_to_the_connected_account_and_once` | `A_due_completed_access_is_paid_as_a_transfer_to_the_connected_account_and_once` |
| `A_due_completed_booking_is_paid_once_with_the_stable_key_and_ends_Sent_with_one_ledger_entry` | `A_due_completed_access_is_paid_once_with_the_stable_key_and_ends_Sent_with_one_ledger_entry` |
| `A_failed_payment_expires_the_booking_and_frees_the_slot` | `A_failed_payment_expires_the_access_and_frees_the_slot` |
| `A_full_refund_after_such_a_dispute_leaves_no_fee_row_so_the_pairs_next_booking_still_earns_the_fee` | `A_full_refund_after_such_a_dispute_leaves_no_fee_row_so_the_pairs_next_access_still_earns_the_fee` |
| `A_future_confirmed_booking_is_cancelled_by_the_practitioner_with_a_full_refund_and_no_client_penalty` | `A_future_confirmed_access_is_cancelled_by_the_agent_with_a_full_refund_and_no_client_penalty` |
| `A_handle_already_linked_to_another_practitioner_is_refused_case_insensitively` | `A_handle_already_linked_to_another_agent_is_refused_case_insensitively` |
| `A_long_offering_title_is_cut_to_what_the_product_name_allows_and_form_characters_survive_encoding` | `A_long_package_title_is_cut_to_what_the_product_name_allows_and_form_characters_survive_encoding` |
| `A_meeting_link_of_a_booking_that_is_not_confirmed_is_not_shown_even_when_the_service_sent_one` | `A_meeting_link_of_a_access_that_is_not_confirmed_is_not_shown_even_when_the_service_sent_one` |
| `A_missing_model_or_offering_or_a_bad_body_is_400` | `A_missing_model_or_package_or_a_bad_body_is_400` |
| `A_new_practitioner_is_marked_new_and_a_rating_below_the_threshold_is_not_shown` | `A_new_agent_is_marked_new_and_a_rating_below_the_threshold_is_not_shown` |
| `A_non_demo_booking_is_never_touched_even_when_the_only_row_present` | `A_non_demo_access_is_never_touched_even_when_the_only_row_present` |
| `A_non_demo_practitioner_is_never_touched_even_when_the_only_row_present` | `A_non_demo_agent_is_never_touched_even_when_the_only_row_present` |
| `A_non_practitioner_resolves_to_nothing_and_is_not_cached` | `A_non_agent_resolves_to_nothing_and_is_not_cached` |
| `A_notification_of_an_unknown_booking_is_a_failed_attempt` | `A_notification_of_an_unknown_access_is_a_failed_attempt` |
| `A_paid_event_for_another_amount_or_currency_is_Rejected_with_an_alert_and_the_booking_stays_unpaid` | `A_paid_event_for_another_amount_or_currency_is_Rejected_with_an_alert_and_the_access_stays_unpaid` |
| `A_pair_the_booking_side_reports_with_two_bookings_is_ignored_even_with_five_stars` | `A_pair_the_access_side_reports_with_two_accesses_is_ignored_even_with_five_stars` |
| `A_pair_with_more_than_two_paid_bookings_is_listed_with_its_count_and_a_pair_with_two_is_not` | `A_pair_with_more_than_two_paid_accesses_is_listed_with_its_count_and_a_pair_with_two_is_not` |
| `A_payout_to_a_practitioner_without_an_account_makes_no_Stripe_call_and_is_counted_as_a_failed_attempt` | `A_payout_to_a_agent_without_an_account_makes_no_Stripe_call_and_is_counted_as_a_failed_attempt` |
| `A_practitioner_answer_is_cached_for_five_minutes_and_asked_again_after` | `A_agent_answer_is_cached_for_five_minutes_and_asked_again_after` |
| `A_practitioner_cancel_also_removes_the_unsent_rows_and_tells_the_client_with_Cancelled_due_now` | `A_agent_cancel_also_removes_the_unsent_rows_and_tells_the_client_with_Cancelled_due_now` |
| `A_practitioner_cancel_always_refunds_in_full_before_the_end` | `A_agent_cancel_always_refunds_in_full_before_the_end` |
| `A_practitioner_cancel_at_or_after_the_end_is_409` | `A_agent_cancel_at_or_after_the_end_is_409` |
| `A_practitioner_is_told_the_client_is_refunded_and_that_they_are_not_paid` | `A_agent_is_told_the_client_is_refunded_and_that_they_are_not_paid` |
| `A_practitioner_is_told_the_client_is_refunded_in_full_and_they_are_not_paid` | `A_agent_is_told_the_client_is_refunded_in_full_and_they_are_not_paid` |
| `A_practitioner_sees_the_panel_in_the_menu_and_the_footer_and_the_directory_is_asked_once_a_minute_per_session` | `A_agent_sees_the_panel_in_the_menu_and_the_footer_and_the_directory_is_asked_once_a_minute_per_session` |
| `A_practitioner_that_is_not_Approved_keeps_the_pushed_values_but_is_not_scored` | `A_agent_that_is_not_Approved_keeps_the_pushed_values_but_is_not_scored` |
| `A_practitioner_who_is_not_Approved_is_422_practitioner_not_bookable` | `A_agent_who_is_not_Approved_is_422_agent_not_bookable` |
| `A_practitioner_without_a_held_address_is_retried_then_given_up_and_never_sent_anywhere_else` | `A_agent_without_a_held_address_is_retried_then_given_up_and_never_sent_anywhere_else` |
| `A_practitioner_without_a_rating_is_marked_with_the_exact_Nowy_badge` | `A_agent_without_a_rating_is_marked_with_the_exact_Nowy_badge` |
| `A_provider_that_is_down_gives_503_and_the_booking_stays_Disputed` | `A_provider_that_is_down_gives_503_and_the_access_stays_Disputed` |
| `A_provider_that_is_down_leaves_the_booking_confirmed_answers_503_and_the_next_call_finishes_it` | `A_provider_that_is_down_leaves_the_access_confirmed_answers_503_and_the_next_call_finishes_it` |
| `A_refused_or_unknown_suspension_does_not_call_Booking` | `A_refused_or_unknown_suspension_does_not_call_Access` |
| `A_registration_that_continues_a_booking_says_so` | `A_registration_that_continues_a_access_says_so` |
| `A_resolved_booking_falls_due_at_the_moment_of_the_resolution` | `A_resolved_access_falls_due_at_the_moment_of_the_resolution` |
| `A_resolved_booking_without_payout_due_at_falls_due_at_its_resolution_time` | `A_resolved_access_without_payout_due_at_falls_due_at_its_resolution_time` |
| `A_review_that_is_not_Published_is_404_to_its_own_practitioner_and_stays_without_a_reply` | `A_review_that_is_not_Published_is_404_to_its_own_agent_and_stays_without_a_reply` |
| `A_reviewable_booking_offers_the_review_as_the_primary_action` | `A_reviewable_access_offers_the_review_as_the_primary_action` |
| `A_second_booking_of_a_long_id_pair_gets_fee_zero_and_the_first_the_fee` | `A_second_access_of_a_long_id_pair_gets_fee_zero_and_the_first_the_fee` |
| `A_second_booking_of_a_pair_gets_fee_zero_through_ON_CONFLICT_DO_NOTHING` | `A_second_access_of_a_pair_gets_fee_zero_through_ON_CONFLICT_DO_NOTHING` |
| `A_second_dispute_of_the_same_booking_is_409` | `A_second_dispute_of_the_same_access_is_409` |
| `A_sign_in_that_continues_a_booking_says_so_and_does_not_print_the_query` | `A_sign_in_that_continues_a_access_says_so_and_does_not_print_the_query` |
| `A_signed_paid_session_confirms_the_booking_once_and_a_redelivery_changes_nothing` | `A_signed_paid_session_confirms_the_access_once_and_a_redelivery_changes_nothing` |
| `A_succeeded_refund_Booking_asked_for_is_RefundSucceeded_for_the_payment_it_carries` | `A_succeeded_refund_Access_asked_for_is_RefundSucceeded_for_the_payment_it_carries` |
| `A_success_and_a_failure_for_one_booking_at_once_apply_exactly_one_and_leave_a_consistent_state` | `A_success_and_a_failure_for_one_access_at_once_apply_exactly_one_and_leave_a_consistent_state` |
| `A_successful_payment_is_simulated_with_the_booking_id_and_the_browser_lands_on_the_confirmation` | `A_successful_payment_is_simulated_with_the_access_id_and_the_browser_lands_on_the_confirmation` |
| `A_suspended_practitioner_activating_an_offer_stays_unlisted_and_has_no_public_profile` | `A_suspended_agent_activating_an_offer_stays_unlisted_and_has_no_public_profile` |
| `A_token_without_a_usable_address_still_creates_the_booking_with_no_snapshot` | `A_token_without_a_usable_address_still_creates_the_access_with_no_snapshot` |
| `A_user_with_more_bookings_than_one_batch_is_erased_completely` | `A_user_with_more_accesses_than_one_batch_is_erased_completely` |
| `A_user_with_several_batches_of_bookings_is_erased_on_PostgreSQL` | `A_user_with_several_batches_of_accesses_is_erased_on_PostgreSQL` |
| `A_valid_signature_confirms_the_booking_snapshots_the_meeting_url_and_writes_the_ClientCharge` | `A_valid_signature_confirms_the_access_snapshots_the_meeting_url_and_writes_the_ClientCharge` |
| `A_verified_practitioner_has_one_success_badge_and_no_other_check` | `A_verified_agent_has_one_success_badge_and_no_other_check` |
| `A_webhook_for_a_booking_that_does_not_exist_yet_is_202_with_Retry_After_and_is_not_recorded` | `A_webhook_for_a_access_that_does_not_exist_yet_is_202_with_Retry_After_and_is_not_recorded` |
| `Activate_refuses_a_sixth_active_offering_and_another_practitioners_offering_is_404` | `Activate_refuses_a_sixth_active_package_and_another_agents_package_is_404` |
| `Activate_turns_a_deactivated_offering_back_on_and_is_idempotent` | `Activate_turns_a_deactivated_package_back_on_and_is_idempotent` |
| `AddBooking` | `AddAccess` |
| `After_expiry_a_new_purchase_of_the_same_offering_succeeds` | `After_expiry_a_new_purchase_of_the_same_package_succeeds` |
| `AllClientBookings` | `AllClientAccesses` |
| `An_Approved_profile_is_public_and_carries_the_card_the_bio_and_only_active_offerings` | `An_Approved_profile_is_public_and_carries_the_card_the_bio_and_only_active_packages` |
| `An_Approved_profile_without_an_active_offering_answers_200_with_no_offerings_and_a_zero_price` | `An_Approved_profile_without_an_active_package_answers_200_with_no_packages_and_a_zero_price` |
| `An_account_with_no_profile_is_onboarded_and_told_that_any_account_may_become_a_practitioner` | `An_account_with_no_profile_is_onboarded_and_told_that_any_account_may_become_a_agent` |
| `An_erased_practitioner_no_longer_appears_in_the_public_listing` | `An_erased_agent_no_longer_appears_in_the_public_listing` |
| `An_event_Booking_does_not_use_is_acknowledged_and_recorded_as_Ignored` | `An_event_Access_does_not_use_is_acknowledged_and_recorded_as_Ignored` |
| `An_expired_session_expires_the_booking_and_an_event_for_a_session_nobody_knows_is_retried_later` | `An_expired_session_expires_the_access_and_an_event_for_a_session_nobody_knows_is_retried_later` |
| `An_express_account_is_created_in_Poland_with_transfers_requested_and_one_key_per_practitioner` | `An_express_account_is_created_in_Poland_with_transfers_requested_and_one_key_per_agent` |
| `An_id_in_the_form_that_is_not_one_of_the_practitioners_offers_creates_a_new_offer_and_never_updates_it` | `An_id_in_the_form_that_is_not_one_of_the_agents_offers_creates_a_new_offer_and_never_updates_it` |
| `An_inactive_offering_is_422_offering_inactive` | `An_inactive_package_is_422_package_inactive` |
| `An_offering_carries_its_kind_as_the_wire_name_and_its_units` | `An_package_carries_its_kind_as_the_wire_name_and_its_units` |
| `An_unknown_booking_and_somebody_elses_are_the_same_404_and_a_visitor_goes_to_sign_in` | `An_unknown_access_and_somebody_elses_are_the_same_404_and_a_visitor_goes_to_sign_in` |
| `An_unknown_booking_is_404` | `An_unknown_access_is_404` |
| `An_unknown_booking_is_404_and_no_token_is_401` | `An_unknown_access_is_404_and_no_token_is_401` |
| `An_unknown_offering_is_404_and_a_Directory_outage_is_503` | `An_unknown_package_is_404_and_a_Directory_outage_is_503` |
| `An_unknown_practitioner_is_NotFound_after_a_single_call` | `An_unknown_agent_is_NotFound_after_a_single_call` |
| `An_unknown_practitioner_is_a_no_op_not_an_error` | `An_unknown_agent_is_a_no_op_not_an_error` |
| `An_unknown_practitioner_is_not_pushed_again_in_a_tight_loop_but_is_retried_later_or_when_it_changes` | `An_unknown_agent_is_not_pushed_again_in_a_tight_loop_but_is_retried_later_or_when_it_changes` |
| `An_unknown_profile_and_an_offering_that_is_not_in_it_are_a_404` | `An_unknown_profile_and_an_package_that_is_not_in_it_are_a_404` |
| `Anonymous_is_401_and_a_non_admin_is_403_even_a_party_of_the_booking` | `Anonymous_is_401_and_a_non_admin_is_403_even_a_party_of_the_access` |
| `Another_practitioner_gets_404_and_nothing_is_written` | `Another_agent_gets_404_and_nothing_is_written` |
| `Another_practitioners_offering_is_404_on_put_and_deactivate_and_absent_from_get` | `Another_agents_package_is_404_on_put_and_deactivate_and_absent_from_get` |
| `Another_signed_in_user_cannot_simulate_someone_elses_booking_and_it_does_not_change` | `Another_signed_in_user_cannot_simulate_someone_elses_access_and_it_does_not_change` |
| `Another_users_booking_is_a_404_not_a_leak` | `Another_users_access_is_a_404_not_a_leak` |
| `Applying_the_proposal_appends_the_edited_text_and_the_booking_is_then_ai_assisted_with_the_problem_slug` | `Applying_the_proposal_appends_the_edited_text_and_the_access_is_then_ai_assisted_with_the_problem_slug` |
| `Approved_practitioners_cover_at_least_three_categories_that_exist_in_the_taxonomy` | `Approved_agents_cover_at_least_three_categories_that_exist_in_the_taxonomy` |
| `Asks_Booking_for_more_than_two_bookings_with_the_page_limit_and_never_more_than_50` | `Asks_Access_for_more_than_two_accesses_with_the_page_limit_and_never_more_than_50` |
| `At_most_five_booking_reads_run_at_once_for_a_page_of_cards` | `At_most_five_access_reads_run_at_once_for_a_page_of_cards` |
| `BookingA` | `AccessA` |
| `BookingAdmin` | `AccessAdmin` |
| `BookingB` | `AccessB` |
| `BookingCalls` | `AccessCalls` |
| `BookingClientTests` | `AccessClientTests` |
| `BookingComponentsTests` | `AccessComponentsTests` |
| `BookingDetailDialogTests` | `AccessDetailDialogTests` |
| `BookingDetailTests` | `AccessDetailTests` |
| `BookingDown` | `AccessDown` |
| `BookingFactory` | `AccessFactory` |
| `BookingFlowModelTests` | `AccessFlowModelTests` |
| `BookingFormTests` | `AccessFormTests` |
| `BookingFormUx020Tests` | `AccessFormUx020Tests` |
| `BookingFormatTests` | `AccessFormatTests` |
| `BookingHost` | `AccessHost` |
| `BookingJobDriver` | `AccessJobDriver` |
| `BookingOf` | `AccessOf` |
| `BookingPermissionsTests` | `AccessPermissionsTests` |
| `BookingSeedFixture` | `AccessSeedFixture` |
| `BookingSeedStepFailureTests` | `AccessSeedStepFailureTests` |
| `BookingSeedStepTests` | `AccessSeedStepTests` |
| `BookingStateMachineTests` | `AccessStateMachineTests` |
| `BookingStepsTests` | `AccessStepsTests` |
| `BookingWorld` | `AccessWorld` |
| `Booking_answering_503_because_the_Directory_was_rate_limited_is_waited_out_too` | `Access_answering_503_because_the_Directory_was_rate_limited_is_waited_out_too` |
| `Booking_down_is_a_503_and_an_account_with_no_profile_is_sent_to_the_dashboard` | `Access_down_is_a_503_and_an_account_with_no_profile_is_sent_to_the_dashboard` |
| `Booking_statuses_are_exactly_the_state_machine_of_CONTRACTS_8_1` | `Access_statuses_are_exactly_the_state_machine_of_CONTRACTS_8_1` |
| `Booking_unavailable_is_503_dependency_unavailable_with_no_detail` | `Access_unavailable_is_503_dependency_unavailable_with_no_detail` |
| `BookingsFail` | `AccessesFail` |
| `BookingsOf` | `AccessesOf` |
| `Bookings_allow_10_a_minute_the_11th_is_429_per_user_and_the_minute_recovers` | `Accesses_allow_10_a_minute_the_11th_is_429_per_user_and_the_minute_recovers` |
| `Bookings_allow_30_a_day_the_31st_is_429_even_when_the_minute_is_free_and_the_day_recovers` | `Accesses_allow_30_a_day_the_31st_is_429_even_when_the_minute_is_free_and_the_day_recovers` |
| `Bookings_exist_in_the_expected_count_per_status_read_through_the_API` | `Accesses_exist_in_the_expected_count_per_status_read_through_the_API` |
| `By_user_of_a_non_practitioner_is_404` | `By_user_of_a_non_agent_is_404` |
| `CancelFutureBookingsApiTests` | `CancelFutureAccessesApiTests` |
| `CancelUnsent_removes_only_unsent_rows_of_the_named_kinds_of_that_booking` | `CancelUnsent_removes_only_unsent_rows_of_the_named_kinds_of_that_access` |
| `Cancel_client_any_time_while_Confirmed_and_unused_practitioner_only_before_the_end` | `Cancel_client_any_time_while_Confirmed_and_unused_agent_only_before_the_end` |
| `Chat_offering_returns_kind_and_units` | `Chat_package_returns_kind_and_units` |
| `Checkout_posts_a_payment_mode_session_in_zloty_with_the_booking_the_idempotency_key_and_the_documented_headers` | `Checkout_posts_a_payment_mode_session_in_zloty_with_the_access_the_idempotency_key_and_the_documented_headers` |
| `Checkout_url_is_site_base_url_checkout_mock_booking_id_and_the_payment_id_is_mock_guid_N` | `Checkout_url_is_site_base_url_checkout_mock_access_id_and_the_payment_id_is_mock_guid_N` |
| `Combined_filters_hold_on_the_same_offering` | `Combined_filters_hold_on_the_same_package` |
| `CompletedBookingsByKey` | `CompletedAccessesByKey` |
| `Completed_bookings_are_reviewable_by_Bookings_internal_endpoint_and_the_others_in_the_review_path_are_not` | `Completed_accesses_are_reviewable_by_Accesses_internal_endpoint_and_the_others_in_the_review_path_are_not` |
| `Completeness_rises_by_a_quarter_with_the_first_active_offering_and_falls_back_after_deactivation` | `Completeness_rises_by_a_quarter_with_the_first_active_package_and_falls_back_after_deactivation` |
| `Completion_of_a_first_booking_takes_the_fee_of_CONTRACTS_9_1` | `Completion_of_a_first_access_takes_the_fee_of_CONTRACTS_9_1` |
| `ConfirmedBooking` | `ConfirmedAccess` |
| `Confirming_a_booking_by_payment_schedules_the_two_confirmations_and_the_review_request` | `Confirming_a_access_by_payment_schedules_the_two_confirmations_and_the_review_request` |
| `Counts_are_per_practitioner_and_add_up` | `Counts_are_per_agent_and_add_up` |
| `CreateBooking` | `CreateAccess` |
| `CreateBookingApiTests` | `CreateAccessApiTests` |
| `CreateBookingIntegrationTests` | `CreateAccessIntegrationTests` |
| `Create_answers_201_with_the_hold_the_checkout_url_and_stores_booking_payment_and_idempotency_record` | `Create_answers_201_with_the_hold_the_checkout_url_and_stores_access_payment_and_idempotency_record` |
| `Creating_a_booking_copies_the_email_claim_of_the_token_into_the_row` | `Creating_a_access_copies_the_email_claim_of_the_token_into_the_row` |
| `Creating_an_offering_on_an_Approved_profile_changes_its_completeness_and_so_its_stored_score` | `Creating_an_package_on_an_Approved_profile_changes_its_completeness_and_so_its_stored_score` |
| `Demo_client_has_a_Completed_booking_inside_its_dispute_window_and_the_API_says_so` | `Demo_client_has_a_Completed_access_inside_its_dispute_window_and_the_API_says_so` |
| `Detail_and_list_carry_the_practitioner_name_for_the_client_and_the_client_name_for_the_practitioner` | `Detail_and_list_carry_the_agent_name_for_the_client_and_the_client_name_for_the_agent` |
| `DirectoryPractitioners` | `DirectoryAgents` |
| `Disabling_or_enabling_an_id_that_is_not_the_practitioners_calls_nothing` | `Disabling_or_enabling_an_id_that_is_not_the_agents_calls_nothing` |
| `Dispute_and_resolution_leave_a_Disputed_and_a_Resolved_booking_with_a_release_decision` | `Dispute_and_resolution_leave_a_Disputed_and_a_Resolved_access_with_a_release_decision` |
| `Each_filter_alone_and_combined_on_the_same_offering` | `Each_filter_alone_and_combined_on_the_same_package` |
| `Each_tile_has_its_own_sentence_and_the_practitioner_count_from_the_sitemap` | `Each_tile_has_its_own_sentence_and_the_agent_count_from_the_sitemap` |
| `Erasing_an_unknown_subject_or_one_that_was_erased_already_does_not_call_Booking` | `Erasing_an_unknown_subject_or_one_that_was_erased_already_does_not_call_Access` |
| `Erasing_the_account_anonymises_every_agent_after_cancelling_each_ones_bookings` | `Erasing_the_account_anonymises_every_agent_after_cancelling_each_ones_accesses` |
| `Erasing_the_client_leaves_the_meeting_link_and_the_offering_title_of_the_practitioner` | `Erasing_the_client_leaves_the_meeting_link_and_the_package_title_of_the_agent` |
| `Erasing_the_client_nulls_the_snapshot_and_erasing_the_practitioner_leaves_the_clients` | `Erasing_the_client_nulls_the_snapshot_and_erasing_the_agent_leaves_the_clients` |
| `Erasing_the_practitioner_clears_the_meeting_link_and_the_offering_title_and_the_client_still_sees_the_booking` | `Erasing_the_agent_clears_the_meeting_link_and_the_package_title_and_the_client_still_sees_the_access` |
| `Erasure_cancels_the_future_bookings_first_and_only_then_anonymises` | `Erasure_cancels_the_future_accesses_first_and_only_then_anonymises` |
| `Erasure_is_refused_with_503_and_leaves_the_row_when_Booking_cannot_cancel_and_a_retry_completes_it` | `Erasure_is_refused_with_503_and_leaves_the_row_when_Access_cannot_cancel_and_a_retry_completes_it` |
| `Even_a_booking_inside_the_24h_window_is_refunded_in_full` | `Even_a_access_inside_the_24h_window_is_refunded_in_full` |
| `Events_are_oldest_first_for_that_practitioner_only_with_actor_note_and_kind` | `Events_are_oldest_first_for_that_agent_only_with_actor_note_and_kind` |
| `Events_of_a_practitioner_without_history_is_an_empty_list_not_404` | `Events_of_a_agent_without_history_is_an_empty_list_not_404` |
| `Every_Billing_BookingStatus_exists_in_the_Contracts_enum_and_vice_versa` | `Every_Billing_AccessStatus_exists_in_the_Contracts_enum_and_vice_versa` |
| `Every_booking_carries_a_demo_key_and_an_idempotency_key_derived_from_it` | `Every_access_carries_a_demo_key_and_an_idempotency_key_derived_from_it` |
| `Every_booking_of_the_plan_buys_a_package_the_agent_really_sells_and_orders_tasks_only_on_task_packages` | `Every_access_of_the_plan_buys_a_package_the_agent_really_sells_and_orders_tasks_only_on_task_packages` |
| `Every_booking_status_the_contract_has_is_mapped_and_the_css_class_follows_the_kind` | `Every_access_status_the_contract_has_is_mapped_and_the_css_class_follows_the_kind` |
| `Every_package_is_of_the_kind_and_size_of_its_offering_and_its_units_used_are_real` | `Every_package_is_of_the_kind_and_size_of_its_package_and_its_units_used_are_real` |
| `Every_seeded_review_lands_on_an_approved_practitioner_so_the_check_above_sees_all_of_them` | `Every_seeded_review_lands_on_an_approved_agent_so_the_check_above_sees_all_of_them` |
| `Exactly_one_review_exists_per_Completed_demo_package_by_that_bookings_client_for_that_bookings_practitioner` | `Exactly_one_review_exists_per_Completed_demo_package_by_that_accesses_client_for_that_accesses_agent` |
| `Exclusion_constraint_rejects_overlapping_ranges_for_one_practitioner` | `Exclusion_constraint_rejects_overlapping_ranges_for_one_agent` |
| `F16_a_profile_with_no_packages_says_so_and_the_booking_service_is_not_asked` | `F16_a_profile_with_no_packages_says_so_and_the_access_service_is_not_asked` |
| `Factor_3_makes_30_practitioners_and_a_second_run_writes_nothing_new` | `Factor_3_makes_30_agents_and_a_second_run_writes_nothing_new` |
| `FakeBooking` | `FakeAccess` |
| `Filters_must_all_hold_on_the_same_offering` | `Filters_must_all_hold_on_the_same_package` |
| `Find_can_ask_for_one_offering` | `Find_can_ask_for_one_package` |
| `For_a_practitioner_the_other_party_is_the_client_and_the_brief_is_the_clients` | `For_a_agent_the_other_party_is_the_client_and_the_brief_is_the_clients` |
| `FromPrice_is_the_lowest_active_offering_and_respects_the_filters` | `FromPrice_is_the_lowest_active_package_and_respects_the_filters` |
| `GetOffering_returns_a_typed_result_and_sends_the_internal_key` | `GetPackage_returns_a_typed_result_and_sends_the_internal_key` |
| `Invalid_input_is_422_and_Booking_is_not_asked` | `Invalid_input_is_422_and_Access_is_not_asked` |
| `It_lists_the_bookings_with_their_hooks_and_loads_the_client_view_by_default` | `It_lists_the_accesses_with_their_hooks_and_loads_the_client_view_by_default` |
| `Journey_3_starts_from_an_approved_practitioner_with_no_review` | `Journey_3_starts_from_an_approved_agent_with_no_review` |
| `Lists_a_pair_with_three_bookings_and_not_one_with_two` | `Lists_a_pair_with_three_accesses_and_not_one_with_two` |
| `Logs_carry_the_booking_id_and_never_the_brief_or_any_other_id` | `Logs_carry_the_access_id_and_never_the_brief_or_any_other_id` |
| `Logs_carry_the_booking_id_and_never_the_key_the_practitioner_or_the_user_ids` | `Logs_carry_the_access_id_and_never_the_key_the_agent_or_the_user_ids` |
| `Me_lists_the_agents_and_keeps_the_first_as_practitionerId` | `Me_lists_the_agents_and_keeps_the_first_as_agentId` |
| `Migration_creates_offerings_with_a_text_array_a_gin_index_and_restricting_foreign_keys` | `Migration_creates_packages_with_a_text_array_a_gin_index_and_restricting_foreign_keys` |
| `Mixed_offering_github_slots` | `Mixed_package_github_slots` |
| `MyBookingsApiTests` | `MyAccessesApiTests` |
| `MyBookingsIntegrationTests` | `MyAccessesIntegrationTests` |
| `MyBookingsScheduleTests` | `MyAccessesScheduleTests` |
| `MyBookingsTests` | `MyAccessesTests` |
| `Names_of_an_unknown_practitioner_is_404` | `Names_of_an_unknown_agent_is_404` |
| `NewBooking` | `NewAccess` |
| `NoBookings` | `NoAccesses` |
| `No_other_booking_is_reviewed_and_the_three_ratings_differ_so_the_averages_differ` | `No_other_access_is_reviewed_and_the_three_ratings_differ_so_the_averages_differ` |
| `No_package_of_that_offering_is_404_model_not_found_and_a_used_up_one_is_429_insufficient_quota` | `No_package_of_that_package_is_404_model_not_found_and_a_used_up_one_is_429_insufficient_quota` |
| `No_template_contains_the_brief_or_any_other_free_text_of_the_booking` | `No_template_contains_the_brief_or_any_other_free_text_of_the_access` |
| `OfferingApiTests` | `PackageApiTests` |
| `OfferingFactory` | `PackageFactory` |
| `OfferingIntegrationTests` | `PackageIntegrationTests` |
| `OfferingKindTests` | `PackageKindTests` |
| `OfferingLookup` | `PackageLookup` |
| `OfferingPriceConfigTests` | `PackagePriceConfigTests` |
| `Offering_lookup_falls_back_to_the_practitioners_default_meeting_url` | `Package_lookup_falls_back_to_the_agents_default_meeting_url` |
| `Offering_lookup_has_no_meeting_url_when_neither_is_set_and_reports_a_suspended_status` | `Package_lookup_has_no_meeting_url_when_neither_is_set_and_reports_a_suspended_status` |
| `Offering_lookup_returns_the_offerings_own_meeting_url` | `Package_lookup_returns_the_packages_own_meeting_url` |
| `OfferingsOf` | `PackagesOf` |
| `Offerings_belong_to_one_agent_and_the_active_limit_is_per_agent` | `Packages_belong_to_one_agent_and_the_active_limit_is_per_agent` |
| `Offerings_on_a_chosen_agent_lock_and_change_only_that_agents_row` | `Packages_on_a_chosen_agent_lock_and_change_only_that_agents_row` |
| `One_booking_in_each_status_moves_the_counts_as_the_rule_says` | `One_access_in_each_status_moves_the_counts_as_the_rule_says` |
| `One_review_of_five_stars_on_a_pair_with_three_bookings_is_listed` | `One_review_of_five_stars_on_a_pair_with_three_accesses_is_listed` |
| `Only_Approved_practitioners_with_an_active_offering_are_listed` | `Only_Approved_agents_with_an_active_package_are_listed` |
| `Only_a_booking_awaiting_payment_or_confirmed_is_upcoming_whatever_its_start_says` | `Only_a_access_awaiting_payment_or_confirmed_is_upcoming_whatever_its_start_says` |
| `Only_a_practitioner_can_onboard_and_an_unauthenticated_call_is_401` | `Only_a_agent_can_onboard_and_an_unauthenticated_call_is_401` |
| `Only_a_succeeded_or_partially_refunded_payment_counts_the_booking_status_does_not_matter` | `Only_a_succeeded_or_partially_refunded_payment_counts_the_access_status_does_not_matter` |
| `Only_an_admin_gets_the_list_and_Booking_is_never_asked_for_anyone_else` | `Only_an_admin_gets_the_list_and_Access_is_never_asked_for_anyone_else` |
| `Only_future_confirmed_bookings_of_that_practitioner_are_touched_and_holds_expire` | `Only_future_confirmed_accesses_of_that_agent_are_touched_and_holds_expire` |
| `Only_listed_practitioners_count_each_counts_once_and_empty_pairs_are_omitted` | `Only_listed_agents_count_each_counts_once_and_empty_pairs_are_omitted` |
| `Only_the_Booking_SMTP_adapter_uses_MailKit_and_exposes_none_of_it` | `Only_the_Access_SMTP_adapter_uses_MailKit_and_exposes_none_of_it` |
| `Only_the_client_may_dispute_a_Completed_booking` | `Only_the_client_may_dispute_a_Completed_access` |
| `Open_slots_are_the_constant_whatever_the_bookings` | `Open_slots_are_the_constant_whatever_the_accesses` |
| `Other_kinds_are_still_sent_after_the_booking_start` | `Other_kinds_are_still_sent_after_the_access_start` |
| `Other_users_bookings_and_the_counterparty_keep_their_own_data_and_still_see_the_booking` | `Other_users_accesses_and_the_counterparty_keep_their_own_data_and_still_see_the_access` |
| `Own_offering_is_422` | `Own_package_is_422` |
| `Parallel_reviews_of_one_practitioner_lose_no_summary_update` | `Parallel_reviews_of_one_agent_lose_no_summary_update` |
| `Pay_sends_one_request_with_a_key_no_start_and_the_trimmed_brief_then_redirects_to_the_checkout_of_the_booking` | `Pay_sends_one_request_with_a_key_no_start_and_the_trimmed_brief_then_redirects_to_the_checkout_of_the_access` |
| `Payouts_maps_the_columns_of_CONTRACTS_4_1_with_a_unique_booking_id_and_a_string_status` | `Payouts_maps_the_columns_of_CONTRACTS_4_1_with_a_unique_access_id_and_a_string_status` |
| `PostBooking` | `PostAccess` |
| `PractitionerAddress` | `AgentAddress` |
| `PractitionerAreaTests` | `AgentAreaTests` |
| `PractitionerBrowsers` | `AgentBrowsers` |
| `PractitionerClientTests` | `AgentClientTests` |
| `PractitionerContractCopiesTests` | `AgentContractCopiesTests` |
| `PractitionerEmail` | `AgentEmail` |
| `PractitionerListTests` | `AgentListTests` |
| `PractitionerResolverTests` | `AgentResolverTests` |
| `PractitionerWorld` | `AgentWorld` |
| `Practitioner_` | `Agent_` |
| `Practitioners_are_ordered_by_slug_with_updated_at_and_categories_by_path` | `Agents_are_ordered_by_slug_with_updated_at_and_categories_by_path` |
| `Put_changes_the_offering_and_leaves_the_others_untouched_and_does_not_reactivate` | `Put_changes_the_package_and_leaves_the_others_untouched_and_does_not_reactivate` |
| `Refunded_bookings_are_subtracted_from_the_count_not_the_whole_pair` | `Refunded_accesses_are_subtracted_from_the_count_not_the_whole_pair` |
| `Reset_calls_Bookings_demo_reset_with_the_internal_key_and_removes_bookings_of_demo_users` | `Reset_calls_Accesses_demo_reset_with_the_internal_key_and_removes_accesses_of_demo_users` |
| `Reset_deletes_demo_bookings_with_their_payments_ledger_idempotency_and_webhook_rows_and_keeps_other_rows` | `Reset_deletes_demo_accesses_with_their_payments_ledger_idempotency_and_webhook_rows_and_keeps_other_rows` |
| `Reset_deletes_demo_slug_practitioners_with_their_offerings_and_events_and_keeps_every_other_row` | `Reset_deletes_demo_slug_agents_with_their_packages_and_events_and_keeps_every_other_row` |
| `Scheduling_the_same_booking_kind_and_user_twice_adds_one_row` | `Scheduling_the_same_access_kind_and_user_twice_adds_one_row` |
| `SeedBooking` | `SeedAccess` |
| `SeedDirectoryAndBookingAsync` | `SeedDirectoryAndAccessAsync` |
| `SeedOffering` | `SeedPackage` |
| `Six_concurrent_creates_give_exactly_five_created_and_one_too_many_offerings` | `Six_concurrent_creates_give_exactly_five_created_and_one_too_many_packages` |
| `Sixth_active_offering_is_too_many_and_deactivating_frees_a_place` | `Sixth_active_package_is_too_many_and_deactivating_frees_a_place` |
| `Someone_elses_booking_is_404_and_nothing_is_written` | `Someone_elses_access_is_404_and_nothing_is_written` |
| `Submit_no_longer_lists_offering_once_an_active_one_exists` | `Submit_no_longer_lists_package_once_an_active_one_exists` |
| `Submit_writes_one_Submitted_event_by_the_practitioner` | `Submit_writes_one_Submitted_event_by_the_agent` |
| `Suspending_asks_Booking_once_to_cancel_that_practitioners_future_bookings_after_the_status_is_saved` | `Suspending_asks_Access_once_to_cancel_that_agents_future_accesses_after_the_status_is_saved` |
| `Suspicious_pairs_are_a_table_with_a_named_practitioner_and_a_short_client_reference` | `Suspicious_pairs_are_a_table_with_a_named_agent_and_a_short_client_reference` |
| `Tech_filter_returns_exactly_the_rows_whose_offering_has_that_tech` | `Tech_filter_returns_exactly_the_rows_whose_package_has_that_tech` |
| `The_address_Booking_gives_Stripe_lands_on_the_page_of_this_host_keeping_only_its_two_flags` | `The_address_Access_gives_Stripe_lands_on_the_page_of_this_host_keeping_only_its_two_flags` |
| `The_authenticated_page_says_so_in_an_alert_when_the_booking_service_is_down_and_the_shell_stays` | `The_authenticated_page_says_so_in_an_alert_when_the_access_service_is_down_and_the_shell_stays` |
| `The_booking_a_dispute_card_reads_is_the_booking_view_with_the_payment_as_its_wire_name` | `The_access_a_dispute_card_reads_is_the_access_view_with_the_payment_as_its_wire_name` |
| `The_booking_column_shows_the_random_tail_with_the_whole_id_in_a_title_and_no_link` | `The_access_column_shows_the_random_tail_with_the_whole_id_in_a_title_and_no_link` |
| `The_booking_detail_with_every_field_the_pages_read` | `The_access_detail_with_every_field_the_pages_read` |
| `The_booking_detail_without_the_optional_parts_has_no_cancel_outcome` | `The_access_detail_without_the_optional_parts_has_no_cancel_outcome` |
| `The_brief_is_hidden_from_the_practitioner_before_confirmation` | `The_brief_is_hidden_from_the_agent_before_confirmation` |
| `The_check_order_is_own_offering_then_practitioner_not_bookable_then_offering_inactive` | `The_check_order_is_own_package_then_agent_not_bookable_then_package_inactive` |
| `The_checkout_exists_exactly_when_the_booking_service_would_accept_the_simulated_payment` | `The_checkout_exists_exactly_when_the_access_service_would_accept_the_simulated_payment` |
| `The_client_address_is_the_booking_snapshot_and_Directory_is_not_asked` | `The_client_address_is_the_access_snapshot_and_Directory_is_not_asked` |
| `The_client_of_the_booking_can_simulate_it_in_the_fake_profile` | `The_client_of_the_access_can_simulate_it_in_the_fake_profile` |
| `The_confirmation_of_a_created_and_confirmed_booking_arrives_at_both_parties_in_correct_Polish_without_secrets_or_brief_text` | `The_confirmation_of_a_created_and_confirmed_access_arrives_at_both_parties_in_correct_Polish_without_secrets_or_brief_text` |
| `The_confirmation_of_an_unknown_booking_is_a_404_and_a_visitor_goes_to_sign_in` | `The_confirmation_of_an_unknown_access_is_a_404_and_a_visitor_goes_to_sign_in` |
| `The_confirmation_reaches_the_client_at_the_token_address_and_the_practitioner_at_the_Directory_address` | `The_confirmation_reaches_the_client_at_the_token_address_and_the_agent_at_the_Directory_address` |
| `The_confirmed_booking_is_titled_as_confirmed_and_any_other_state_keeps_the_plain_title` | `The_confirmed_access_is_titled_as_confirmed_and_any_other_state_keeps_the_plain_title` |
| `The_featured_row_shows_three_practitioners_as_card_links_with_their_price` | `The_featured_row_shows_three_agents_as_card_links_with_their_price` |
| `The_footer_offers_the_way_in_to_a_visitor_and_to_a_client_and_the_panel_to_a_practitioner` | `The_footer_offers_the_way_in_to_a_visitor_and_to_a_client_and_the_panel_to_a_agent` |
| `The_four_practitioner_actions_post_to_their_own_path_and_only_reject_and_suspend_carry_the_trimmed_reason` | `The_four_agent_actions_post_to_their_own_path_and_only_reject_and_suspend_carry_the_trimmed_reason` |
| `The_full_path_works_from_a_booking_created_and_paid_through_the_API` | `The_full_path_works_from_a_access_created_and_paid_through_the_API` |
| `The_hold_moves_with_Booking_HoldMinutes_and_the_checkout_url_with_Site_BaseUrl` | `The_hold_moves_with_Access_HoldMinutes_and_the_checkout_url_with_Site_BaseUrl` |
| `The_idempotency_key_is_stable_per_booking_and_reason_and_a_replayed_provider_confirmation_does_not_double_record` | `The_idempotency_key_is_stable_per_access_and_reason_and_a_replayed_provider_confirmation_does_not_double_record` |
| `The_join_is_by_the_pair_not_by_the_client_or_the_practitioner_alone` | `The_join_is_by_the_pair_not_by_the_client_or_the_agent_alone` |
| `The_json_parses_and_names_the_practitioner_the_offers_and_the_links` | `The_json_parses_and_names_the_agent_the_offers_and_the_links` |
| `The_method_path_query_body_and_the_four_headers_reach_Booking_and_the_answer_comes_back_as_it_is` | `The_method_path_query_body_and_the_four_headers_reach_Access_and_the_answer_comes_back_as_it_is` |
| `The_other_admin_actions_do_not_touch_bookings` | `The_other_admin_actions_do_not_touch_accesses` |
| `The_payouts_table_exists_and_booking_id_is_unique` | `The_payouts_table_exists_and_access_id_is_unique` |
| `The_pending_booking_has_a_live_hold_and_the_expired_one_does_not` | `The_pending_access_has_a_live_hold_and_the_expired_one_does_not` |
| `The_practitioner_address_comes_from_Directory_with_the_internal_key_and_the_user_id_escaped` | `The_agent_address_comes_from_Directory_with_the_internal_key_and_the_user_id_escaped` |
| `The_practitioner_is_always_told_full_refund_and_a_booking_that_cannot_be_cancelled_has_no_outcome` | `The_agent_is_always_told_full_refund_and_a_access_that_cannot_be_cancelled_has_no_outcome` |
| `The_practitioner_may_still_withdraw_a_package_that_was_used` | `The_agent_may_still_withdraw_a_package_that_was_used` |
| `The_practitioner_of_the_booking_is_not_its_client_and_cannot_simulate_either` | `The_agent_of_the_access_is_not_its_client_and_cannot_simulate_either` |
| `The_practitioner_page` | `The_agent_page` |
| `The_practitioner_panel_is_for_practitioners_and_everybody_else_signed_in_is_offered_the_way_in` | `The_agent_panel_is_for_agents_and_everybody_else_signed_in_is_offered_the_way_in` |
| `The_practitioner_sees_brief_and_client_name_from_Confirmed_onward_and_the_client_always` | `The_agent_sees_brief_and_client_name_from_Confirmed_onward_and_the_client_always` |
| `The_rebook_target_is_the_profile_of_a_finished_booking_only` | `The_rebook_target_is_the_profile_of_a_finished_access_only` |
| `The_reference_is_the_random_tail_so_two_bookings_from_the_same_moment_tell_apart` | `The_reference_is_the_random_tail_so_two_accesses_from_the_same_moment_tell_apart` |
| `The_resolution_note_of_a_Resolved_booking_is_for_the_admin_only_and_a_cancel_reason_stays_for_the_parties` | `The_resolution_note_of_a_Resolved_access_is_for_the_admin_only_and_a_cancel_reason_stays_for_the_parties` |
| `The_resolve_form_offers_its_three_options_as_option_cards_in_an_option_group_and_the_booking_id_is_text_not_a_link` | `The_resolve_form_offers_its_three_options_as_option_cards_in_an_option_group_and_the_access_id_is_text_not_a_link` |
| `The_response_is_the_booking_view_without_the_dispute_text_or_the_note_and_neither_is_logged` | `The_response_is_the_access_view_without_the_dispute_text_or_the_note_and_neither_is_logged` |
| `The_reviewed_practitioner_replies_once_and_the_public_list_shows_the_reply` | `The_reviewed_agent_replies_once_and_the_public_list_shows_the_reply` |
| `The_role_tabs_show_only_for_an_account_with_a_practitioner_profile_or_when_the_directory_cannot_say` | `The_role_tabs_show_only_for_an_account_with_a_agent_profile_or_when_the_directory_cannot_say` |
| `The_same_client_with_two_practitioners_is_two_pairs_counted_separately` | `The_same_client_with_two_agents_is_two_pairs_counted_separately` |
| `The_same_key_and_body_twice_returns_the_original_response_and_creates_one_booking_and_one_payment` | `The_same_key_and_body_twice_returns_the_original_response_and_creates_one_access_and_one_payment` |
| `The_session_cookie_and_every_other_cookie_never_reach_Booking_and_the_answers_cookie_never_reaches_the_client` | `The_session_cookie_and_every_other_cookie_never_reach_Access_and_the_answers_cookie_never_reaches_the_client` |
| `The_set_is_practitioners_with_bookings_only` | `The_set_is_agents_with_accesses_only` |
| `The_sitemap_lists_every_approved_practitioner_and_only_the_indexable_categories` | `The_sitemap_lists_every_approved_agent_and_only_the_indexable_categories` |
| `The_status_is_none_then_pending_then_ready_and_a_ready_account_lets_the_booking_through` | `The_status_is_none_then_pending_then_ready_and_a_ready_account_lets_the_access_through` |
| `The_step_runs_after_Booking_and_before_Reputation` | `The_step_runs_after_Access_and_before_Reputation` |
| `The_step_runs_after_Booking_and_is_registered_once` | `The_step_runs_after_Access_and_is_registered_once` |
| `The_suspended_practitioner_claims_no_reviews_because_the_seeder_makes_no_booking_for_them` | `The_suspended_agent_claims_no_reviews_because_the_seeder_makes_no_access_for_them` |
| `The_task_list_is_read_in_the_snake_case_shape_Booking_writes_with_both_dates` | `The_task_list_is_read_in_the_snake_case_shape_Access_writes_with_both_dates` |
| `The_unique_index_refuses_a_second_review_of_one_booking_even_when_the_application_check_is_bypassed` | `The_unique_index_refuses_a_second_review_of_one_access_even_when_the_application_check_is_bypassed` |
| `The_unique_index_rejects_a_duplicate_booking_kind_and_user` | `The_unique_index_rejects_a_duplicate_access_kind_and_user` |
| `The_visitor_address_goes_to_Booking_with_the_proxy_key_and_the_visitors_own_values_are_never_copied` | `The_visitor_address_goes_to_Access_with_the_proxy_key_and_the_visitors_own_values_are_never_copied` |
| `TwoBookings` | `TwoAccesses` |
| `Two_bookings_made_in_the_same_moment_still_read_differently` | `Two_accesses_made_in_the_same_moment_still_read_differently` |
| `Two_clients_buying_the_same_offering_in_the_same_second_both_get_201` | `Two_clients_buying_the_same_package_in_the_same_second_both_get_201` |
| `Two_concurrent_cancels_of_one_booking_give_one_200_one_409_and_one_refund_row` | `Two_concurrent_cancels_of_one_access_give_one_200_one_409_and_one_refund_row` |
| `Two_concurrent_completions_of_two_first_bookings_of_one_pair_write_one_fee_row` | `Two_concurrent_completions_of_two_first_accesses_of_one_pair_write_one_fee_row` |
| `Two_concurrent_disputes_of_one_booking_give_one_200_and_one_409_and_the_winners_text_is_kept` | `Two_concurrent_disputes_of_one_access_give_one_200_and_one_409_and_the_winners_text_is_kept` |
| `Two_concurrent_resolves_of_one_booking_give_one_200_and_one_409_and_one_refund` | `Two_concurrent_resolves_of_one_access_give_one_200_and_one_409_and_one_refund` |
| `Two_failures_are_BookingUnavailable_after_exactly_two_calls_and_the_message_has_no_url_or_key` | `Two_failures_are_AccessUnavailable_after_exactly_two_calls_and_the_message_has_no_url_or_key` |
| `Two_job_instances_at_once_pay_each_booking_exactly_once_whatever_the_interleaving` | `Two_job_instances_at_once_pay_each_access_exactly_once_whatever_the_interleaving` |
| `Two_payout_jobs_at_once_pay_each_booking_exactly_once_for_every_interleaving` | `Two_payout_jobs_at_once_pay_each_access_exactly_once_for_every_interleaving` |
| `Two_requests_with_the_same_key_at_once_create_one_booking_and_both_answer_with_it` | `Two_requests_with_the_same_key_at_once_create_one_access_and_both_answer_with_it` |
| `Two_requests_with_the_same_key_at_once_create_one_booking_and_both_answers_carry_the_same_booking_id` | `Two_requests_with_the_same_key_at_once_create_one_access_and_both_answers_carry_the_same_access_id` |
| `Two_reviews_for_one_booking_give_409_already_reviewed_and_the_summary_counts_one` | `Two_reviews_for_one_access_give_409_already_reviewed_and_the_summary_counts_one` |
| `Two_simultaneous_reviews_of_one_booking_give_exactly_one_201_and_one_409` | `Two_simultaneous_reviews_of_one_access_give_exactly_one_201_and_one_409` |
| `Unknown_booking_is_404_not_found` | `Unknown_access_is_404_not_found` |
| `Unknown_offering_is_404` | `Unknown_package_is_404` |
| `Unknown_practitioner_is_404` | `Unknown_agent_is_404` |
| `User_without_a_practitioner_gets_404_on_both_endpoints` | `User_without_a_agent_gets_404_on_both_endpoints` |
| `User_without_a_practitioner_profile_gets_404_on_all_four_verbs` | `User_without_a_agent_profile_gets_404_on_all_four_verbs` |
| `When_Booking_does_not_answer_the_client_gets_502_in_the_OpenAI_shape_without_the_exception_text` | `When_Access_does_not_answer_the_client_gets_502_in_the_OpenAI_shape_without_the_exception_text` |
| `When_Booking_fails_the_profile_stays_suspended_and_the_answer_says_the_cancellation_must_be_repeated` | `When_Access_fails_the_profile_stays_suspended_and_the_answer_says_the_cancellation_must_be_repeated` |
| `When_Directory_is_down_the_view_still_renders_with_a_generic_practitioner_label` | `When_Directory_is_down_the_view_still_renders_with_a_generic_agent_label` |
| `When_booking_is_down_the_dashboard_still_renders_and_shows_no_false_banner` | `When_access_is_down_the_dashboard_still_renders_and_shows_no_false_banner` |
| `When_the_service_sends_the_outcome_of_this_booking_the_dialog_says_it_and_does_not_repeat_the_rule_F11` | `When_the_service_sends_the_outcome_of_this_access_the_dialog_says_it_and_does_not_repeat_the_rule_F11` |
| `With_a_ready_account_the_booking_is_created_with_a_Stripe_checkout_and_a_payment_row_for_the_session` | `With_a_ready_account_the_access_is_created_with_a_Stripe_checkout_and_a_payment_row_for_the_session` |
| `With_the_Mock_provider_there_is_no_onboarding_and_a_booking_needs_no_account` | `With_the_Mock_provider_there_is_no_onboarding_and_a_access_needs_no_account` |
| `Without_a_Booking_endpoint_the_step_says_so_and_does_nothing` | `Without_a_Access_endpoint_the_step_says_so_and_does_nothing` |
| `Without_a_name_in_the_booking_the_summary_leaves_the_row_out_instead_of_printing_a_placeholder_F03` | `Without_a_name_in_the_access_the_summary_leaves_the_row_out_instead_of_printing_a_placeholder_F03` |
| `_practitionersDown` | `_agentsDown` |
| `asPractitioner` | `asAgent` |
| `bookingCalls` | `accessCalls` |
| `bookingReadable` | `accessReadable` |
| `canDispute_is_true_for_a_Completed_booking_until_end_plus_72h` | `canDispute_is_true_for_a_Completed_access_until_end_plus_72h` |
| `dupBooking` | `dupAccess` |
| `otherBooking` | `otherAccess` |
| `perPractitioner` | `perAgent` |
| `practitionerCancelCount` | `agentCancelCount` |
| `practitionerKey` | `agentKey` |
| `practitionerName` | `agentName` |
| `practitionerOverride` | `agentOverride` |
| `practitionerStatus` | `agentStatus` |
| `practitionerToken` | `agentToken` |
| `practitionerUser` | `agentUser` |
| `practitionerUserId` | `agentUserId` |
| `seenByBooking` | `seenByAccess` |
| `toPractitioner` | `toAgent` |
| `withBooking` | `withAccess` |
