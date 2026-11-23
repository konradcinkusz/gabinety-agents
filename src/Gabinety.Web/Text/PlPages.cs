namespace Gabinety.Web.Text;

/// <summary>
/// The Polish text of the public pages (SPEC 5 rule 1), beside <see cref="Pl"/> for size: strings are verbatim from the Next.js catalogue (<c>web/src/components/static/strings.ts</c>,
/// <c>profile/strings.ts</c>, <c>slots/strings.ts</c>), so the journeys find the same names on either host. Improvements of the UI audit are marked with the finding id.
/// </summary>
public static class PlPages
{
    public static class HomeExtra
    {
        public const string MoreHow = "Szczegóły: jak to działa", RulesLink = "Zasady rankingu", PricingLink = "Cennik";

        // Hero: example queries, each a link to the list with that search (UX-011, V-1).
        public const string ExamplesLead = "Na przykład:";
        public const string ExampleAudit = "audyt kodu", ExampleMigration = "migracja .NET", ExampleAi = "integracja AI";

        // Category tiles: one distinct sentence per seeded problem (slug -> text), a generic sentence for a slug this map does not know, and the count in Polish plural forms.
        public static readonly IReadOnlyDictionary<string, string> CategoryDescriptions = new Dictionary<string, string>
        {
            ["audyt-kodu"] = "Przegląd istniejącego kodu: jakość, bezpieczeństwo i plan poprawek.",
            ["migracja-legacy-dotnet"] = "Przeniesienie starszej aplikacji .NET na nowszą wersję, krok po kroku.",
            ["integracja-ai"] = "Dołączenie modeli językowych i agentów AI do istniejącego systemu.",
            ["automatyzacja-procesow"] = "Zautomatyzowanie powtarzalnych zadań i przepływów danych.",
            ["architektura-systemu"] = "Ocena i projekt struktury systemu, granic usług i danych.",
            ["code-review"] = "Druga para oczu na zmiany w kodzie przed wdrożeniem.",
            ["wycena-projektu"] = "Oszacowanie zakresu, czasu i kosztów, zanim zaczniesz projekt.",
            ["rozwiazanie-awarii"] = "Pilna pomoc przy awarii: diagnoza, przywrócenie działania i wnioski.",
        };
        public const string CategoryFallback = "Agent AI w obszarze: {0}.";
        public const string PractitionersOne = "{0} agent", PractitionersFew = "{0} agenci", PractitionersMany = "{0} agentów";

        // Trust band: the verified count is the real number from the sitemap; the statements are the existing ones.
        public const string TrustLabel = "Dlaczego warto";
        public const string VerifiedOne = "{0} zatwierdzony agent", VerifiedFew = "{0} zatwierdzonych agentów", VerifiedMany = "{0} zatwierdzonych agentów";
        public const string TrustPayment = "Płatność z góry, pełny zwrot do pierwszego użycia pakietu";
        public const string TrustRanking = "Ranking bez płatnych miejsc";

        // Featured row: a few practitioners from the first page of the list. A region that fails soft: no heading, no message, when there is nothing to show.
        public const string FeaturedTitle = "Agenci AI", FeaturedIntro = "Kilku agentów z listy. Pełna lista jest na stronie agentów.";
        public const string FeaturedFrom = "od {0}";

        // Call to action for providers.
        public const string ForPractitionersTitle = "Udostępniasz agenta AI?";
        public const string ForPractitionersBody = How.P1 + " " + How.P2;
        public const string ForPractitionersLink = "Dołącz jako dostawca";

        // Questions and answers. Every rule is the wording of the Pricing, How and Pl.Home texts; no amount is written here: the price list is /cennik.
        public const string FaqTitle = "Pytania i odpowiedzi";
        public const string FaqPayWhenQ = "Kiedy płacę?";
        public const string FaqPayWhenA = "Płacisz z góry, przy wykupie dostępu. Pakiet jest aktywny po potwierdzeniu płatności i ważny 30 dni.";
        public const string FaqCancelQ = "Czy mogę zrezygnować z pakietu?";
        public const string FaqCancelA = How.Pay1 + " " + How.Pay2;
        public const string FaqFeeQ = "Ile płacę platformie?";
        public const string FaqFeeA = Pricing.Intro + " Prowizja platformy dotyczy tylko pierwszego pakietu danej pary klient i dostawca. " + Pricing.Later;
        public const string FaqFeeLink = "Zobacz cennik";
        public const string FaqDisputeQ = "Co, jeśli agent nie zrobił tego, co obiecał?";
        public const string FaqDisputeA = How.Pay3;
    }

    public static class ListExtra
    {
        public const string Verified = "Zweryfikowany";
    }

    public static class How
    {
        public const string MetaTitle = "Jak to działa | Gabinety Tech", MetaDescription = "Od wyboru agenta po opinię: kolejne kroki wykupu dostępu, płatności i zwroty.";
        public const string Title = "Jak to działa", Intro = "Gabinety Tech łączy klientów z agentami AI, do których można wykupić dostęp: pakiet zadań albo API czatu.";
        public const string ClientTitle = "Dla klienta";
        public const string C1 = "Wybierz problem i technologię na liście agentów albo wyszukaj po słowie.";
        public const string C2 = "Sprawdź profil agenta: pakiety, ceny, opinie i plakietki weryfikacji.";
        public const string C3 = "Wybierz pakiet i opisz, do czego chcesz użyć agenta. Opis jest widoczny tylko dla dostawcy.";
        public const string C4 = "Zapłać z góry. Dostęp jest aktywny po potwierdzeniu płatności i ważny 30 dni.";
        public const string C5 = "Zlecaj zadania w panelu albo użyj klucza API. Po skorzystaniu z agenta możesz wystawić opinię.";
        public const string PaymentTitle = "Płatności i zwroty";
        public const string Pay1 = "Rezygnacja z pakietu przed pierwszym użyciem oznacza pełny zwrot.";
        public const string Pay2 = "Po pierwszym użyciu rezygnacja nie jest możliwa.";
        public const string Pay3 = "Jeśli agent zadziałał inaczej, niż opisano, możesz zgłosić reklamację do 72 godzin po końcu ważności pakietu. Rozstrzyga ją administrator.";
        public const string PractitionerTitle = "Dla dostawcy";
        public const string P1 = "Załóż konto, uzupełnij profil agenta i dodaj pakiety.";
        public const string P2 = "Administrator sprawdza profil. Po zatwierdzeniu pojawiasz się na liście.";
        public const string P3 = "Wypłata następuje po zakończeniu pakietu, o ile nikt nie zgłosił reklamacji.";
        public const string SeeAlso = "Zobacz też";
        public const string CtaTitle = "Gotowy, żeby zacząć?", CtaBody = "Wybierz agenta i pakiet. Płacisz z góry, a dostęp jest aktywny po potwierdzeniu płatności.", Cta = "Zobacz agentów";
    }

    public static class Ranking
    {
        public const string MetaTitle = "Zasady rankingu | Gabinety Tech", MetaDescription = "Jak ustalamy kolejność agentów: wagi, stałe i przykład obliczenia. Nie da się za nią zapłacić.";
        public const string Title = "Zasady rankingu", Intro = "Kolejność agentów na listach wynika z jednego wzoru. Publikujemy go w całości razem z wersją i datą obowiązywania.";
        public const string Version = "Wersja zasad: {0}", EffectiveFrom = "Obowiązuje od: {0}";
        public const string NothingPaid = "Za miejsce w rankingu nie można zapłacić: żadna opłata, plan ani promocja nie jest daną wejściową wzoru.";
        public const string TieBreak = "Przy równej liczbie punktów wcześniej zatwierdzony profil jest wyżej.";
        public const string WeightsTitle = "Wagi", WeightsCaption = "Składniki wyniku i ich wagi";
        public const string ColPart = "Składnik", ColWeight = "Waga", ColMeaning = "Co mierzy";
        public const string Rating = "Opinie", Reliability = "Rzetelność", Availability = "Dostępność", Completeness = "Kompletność profilu";
        public const string RatingMeaning = "Średnia ocena z opublikowanych opinii, skorygowana tak, by mała próba nie dawała skrajnego wyniku.";
        public const string ReliabilityMeaning = "Odsetek zrealizowanych pakietów w stosunku do wycofanych przez dostawcę.";
        public const string AvailabilityMeaning = "Dostępność agenta zgłaszana do rankingu (agent jest zawsze dostępny).";
        public const string CompletenessMeaning = "Jak dokładnie wypełniony jest profil.";
        public const string ScoreLine = "Wynik to suma składników pomnożonych przez wagi, przeliczona na skalę od 0 do 100.";
        public const string ConstantsTitle = "Stałe", ConstantsCaption = "Stałe używane we wzorze", ColConstant = "Stała", ColValue = "Wartość";
        public const string ConstM = "Waga założenia wstępnego (liczba fikcyjnych opinii)", ConstMu = "Ocena założenia wstępnego";
        public const string ConstMin = "Najmniej opinii, od których pokazujemy średnią ocenę", ConstSlots = "Wartość dostępności, od której dostępność daje pełne punkty";
        public const string SmallSample = "Poniżej tylu opinii przy profilu widać napis „Nowy”, a nie średnią: mała próba nie jest średnią.";
        public const string ExampleTitle = "Przykład obliczenia";
        public const string ExampleIntro = "Agent z {0} opiniami o średniej {1}, {2} zrealizowanymi pakietami, {3} wycofanym przez dostawcę, profilem wypełnionym w {4} i dostępnością {5}.";
        public const string ExampleCaption = "Obliczenie przykładowego wyniku", ColInput = "Wartość składnika (0 do 1)", ColContribution = "Punkty", ExampleTotal = "Wynik razem";
        public const string ScrollRegion = "Tabela: {0}. Przewijana poziomo.";
        public const string Unavailable = "Nie udało się teraz wczytać zasad rankingu. Pokazujemy je zawsze z aktualnych danych, więc spróbuj ponownie za chwilę.";
    }

    public static class Pricing
    {
        public const string MetaTitle = "Cennik | Gabinety Tech", MetaDescription = "Klient płaci cenę pakietu. Prowizja platformy dotyczy tylko pierwszego pakietu danej pary klient i dostawca.";
        public const string Title = "Cennik", Intro = "Klient płaci cenę pakietu agenta, bez dodatkowych opłat.";
        public const string Rule = "Prowizja platformy to {0} ceny pierwszego zrealizowanego pakietu danej pary klient i dostawca.";
        public const string OnlyFirst = "Prowizja dotyczy: tylko pierwszy pakiet.";
        public const string Later = "Za kolejne pakiety tej samej pary platforma nie pobiera nic.";
        public const string Absorbed = "Koszty operatora płatności pokrywa platforma.";
        public const string Rounding = "Prowizję zaokrąglamy do pełnego grosza w górę od połowy.";
        public const string ExampleTitle = "Przykład", ExampleCaption = "Podział ceny pierwszego pakietu", ColItem = "Pozycja", ColAmount = "Kwota";
        public const string Price = "Cena pakietu", FeeRow = "Prowizja platformy", Payout = "Dla dostawcy";
        public const string RefundNote = "Po pełnym zwrocie prowizji nie ma. Przy częściowym zwrocie liczymy ją od kwoty, która zostaje.";
        public const string Example2Title = "Drugi przykład", Example2Intro = "Ta sama zasada dla tańszego pakietu: prowizja liczona od ceny i zaokrąglana do pełnego grosza.";
        public const string Example2Caption = "Podział ceny pakietu {0}", FindLink = "Zobacz agentów i ich ceny";
    }

    public static class Legal
    {
        public const string TermsMetaTitle = "Regulamin | Gabinety Tech", TermsMetaDescription = "Regulamin serwisu Gabinety Tech.", TermsTitle = "Regulamin";
        public const string PrivacyMetaTitle = "Polityka prywatności | Gabinety Tech", PrivacyMetaDescription = "Polityka prywatności serwisu Gabinety Tech.", PrivacyTitle = "Polityka prywatności";
        public const string DraftNotice = "Wersja robocza: tekst nie został jeszcze zatwierdzony przez prawnika i nie obowiązuje.";
        public const string Version = "Wersja: {0}", Date = "Data: {0}", Contents = "Spis treści", Unavailable = "Nie udało się wczytać tekstu. Spróbuj ponownie za chwilę.";
    }

    public static class Profile
    {
        public const string MetaTitle = "{0}: agent AI | Gabinety Tech", MetaDescription = "{0}. {1} Wykup dostęp do agenta AI.";
        public const string OfferingsTitle = "Pakiety", AboutTitle = "O mnie", Duration = "{0} min", Book = "Wykup dostęp", BookOffering = "Wykup dostęp: {0}";
        public const string Portfolio = "Portfolio", Github = "GitHub", Completed = "Zrealizowane pakiety: {0}", MemberSince = "Na platformie od {0}";
        public const string IsNew = "Nowy", Rating = "Ocena {0} ({1})", BadgesLabel = "Weryfikacje", FactsLabel = "Dane agenta", LinksLabel = "Linki";
        public const string NoOfferings = "Ten agent nie ma teraz aktywnych pakietów.";
        public const string Email = "E-mail zweryfikowany", EmailInfo = "Adres e-mail dostawcy tego agenta został potwierdzony.";
        public const string GithubBadge = "GitHub połączony", GithubInfo = "Konto GitHub należy do dostawcy tego agenta: potwierdził to kodem w publicznym profilu.";
        public const string Nip = "NIP zweryfikowany", NipInfo = "Numer NIP działalności dostawcy tego agenta został sprawdzony.";
        public const string Approved = "Zatwierdzony", ApprovedInfo = "Profil został sprawdzony i zatwierdzony przez zespół Gabinety Tech.";
        public const string ReviewsTitle = "Opinie", ReviewsNew = "Nowy na platformie: ten agent nie ma jeszcze opinii.", ReviewsEmpty = "Nie ma jeszcze opublikowanych opinii.";
        public const string ReviewsUnavailable = "Opinie są chwilowo niedostępne. Spróbuj za chwilę.", Retry = "Spróbuj ponownie", Reviewer = "Zweryfikowany klient";
        public const string ReviewRating = "Ocena: {0} z 5", ReviewsMore = "Pokaż więcej opinii", ReplyHeading = "Odpowiedź dostawcy", ReviewsSummary = "Średnia ocen {0} na podstawie opinii: {1}";
        // F-09, P-PROF-3: the 404 of a profile names what is missing.
        public const string NotFoundTitle = "Nie znaleziono agenta", NotFoundBody = "Ten profil nie istnieje albo agent nie jest już widoczny w serwisie.";
        public const string NotFoundAll = "Zobacz wszystkich agentów", NotFoundCategories = "Albo wybierz problem";
        public const string Unavailable = "Nie udało się teraz wczytać profilu. Spróbuj ponownie za chwilę.";
        // UX-014: header checks, summary panel, review summary.
        public const string ChecksTitle = "Co sprawdziliśmy", RatingShort = "★ {0} ({1})", TechLabel = "Technologie";
        public const string SummaryTitle = "Podsumowanie", PriceFromLabel = "od", DurationRange = "{0}–{1} min", SeeTimes = "Zobacz pakiety";
        public const string PaymentNote = "Płatność z góry, pełny zwrot do pierwszego użycia pakietu";
        public const string ReviewsSummaryLine = "★ {0} · {1} {2}", OpinionOne = "opinia", OpinionFew = "opinie", OpinionMany = "opinii";
        // C-01: what a package is. Tasks: "10 zadań, typowy czas ok. 30 min"; Chat: "500 tys. tokenów, API zgodne z OpenAI". Validity is the Access:ValidityDays default (a proposal: CONTRACTS section 9 is the owner's).
        public const string TasksLine = "{0} {1}, typowy czas ok. {2} min", TaskOne = "zadanie", TaskFew = "zadania", TaskMany = "zadań";
        public const string ChatLine = "{0} tys. tokenów, API zgodne z OpenAI", ValidityLine = "Ważność: {0} dni", ValidityDays = "30";
        // C-01: the "how to connect" section: the base address, the model name (the agent's slug) and a curl example.
        public const string ConnectTitle = "Jak się połączyć", ConnectIntro = "Pakiet Czat API działa z narzędziami zgodnymi z OpenAI. Klucz API utworzysz po wykupieniu dostępu, w zakładce Klucze API.";
        public const string ConnectBaseUrl = "Adres bazowy API", ConnectModel = "Nazwa modelu", ConnectExample = "Przykład (curl)", ConnectKeyPlaceholder = "TWÓJ_KLUCZ_API";
        public const string ConnectMessage = "Cześć";
    }

    public static class Nav
    {
        public const string Breadcrumb = "Okruszki", Home = "Strona główna";
    }
}
