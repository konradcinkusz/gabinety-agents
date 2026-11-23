namespace Gabinety.Web.Text;

/// <summary>
/// The Polish text of the purchase flow: the form, the checkout and the confirmation (SPEC 5 rule 1: one place per area). C-02 of docs/agentic/PLAN.md: a package has no time slot, so the flow is
/// Pakiet, Płatność, Gotowe.
/// </summary>
public static class BookFlowText
{
    public const string Title = "Wykup dostępu", MetaTitle = "Wykup dostępu | Gabinety Tech";
    public const string MissingSlot = "Adres jest niepełny. Wybierz pakiet na profilu agenta.";
    public const string BackToSearch = "Wróć do wyszukiwarki", ToProfile = "Wróć do profilu agenta", ProfileUnavailable = "Nie udało się teraz wczytać pakietu. Spróbuj ponownie za chwilę.";
    public const string Retry = "Spróbuj ponownie";

    public static class Summary
    {
        public const string Title = "Podsumowanie", Offering = "Pakiet", Practitioner = "Agent", Units = "Zawartość", Validity = "Ważność", Price = "Do zapłaty";
        public const string ValidFor = "{0} dni od zakupu", ValidUntil = "do {0}";
        public const string Hold = "Zakup jest wstrzymany na 10 minut od kliknięcia „Zapłać”.";
        /// <summary>The refund rule in one line, before the money is spent: it is the lifecycle rule of docs/agentic/PLAN.md 2.5, applied by the Booking service.</summary>
        public const string RefundRule = "Do pierwszego użycia możesz zrezygnować z pełnym zwrotem.";
        public const string TasksLine = "{0} {1}, typowy czas ok. {2} min", ChatLine = "{0} tys. tokenów, API zgodne z OpenAI";
    }

    /// <summary>The three steps of the purchase (<c>BookingSteps</c>); the checkout and the confirmation show the same list with another current step.</summary>
    public static class Steps
    {
        public const string Label = "Kroki wykupu", Done = "(ukończony)";
        public static readonly string[] Names = ["Pakiet", "Płatność", "Gotowe"];
    }

    public static class Brief
    {
        public const string Label = "Do czego chcesz użyć agenta?", Hint = "Od 20 do 2000 znaków. Opis zobaczy dostawca agenta po opłaceniu pakietu.";
        public const string TooShort = "Opis musi mieć co najmniej 20 znaków.", TooLong = "Opis może mieć najwyżej 2000 znaków.";
        public const string Counter = "{0} / 2000";
    }

    public static class Errors
    {
        /// <summary>Heading of the form summary (DESIGN-CONTRACT 5.5): "Popraw 1 pole", "Popraw 2 pola", "Popraw 5 pól".</summary>
        public const string SummaryOne = "Popraw 1 pole", SummaryFew = "Popraw {0} pola", SummaryMany = "Popraw {0} pól";
        public const string Unavailable = "Nie udało się połączyć z usługą. Spróbuj ponownie: nie zostaniesz obciążony dwa razy.";
        public const string RateLimited = "Zbyt wiele prób. Odczekaj chwilę i spróbuj ponownie.";
        public const string SlotTaken = "Nie udało się zarezerwować pakietu. Spróbuj ponownie.";
        public const string Inactive = "Ten pakiet nie jest już dostępny.", Own = "Nie możesz wykupić własnego pakietu.", NotBookable = "Ten agent nie sprzedaje teraz pakietów.";
        /// <summary>422 <c>practitioner_payments_not_ready</c> (ADR 0011): the provider has no connected payment account that can receive money yet.</summary>
        public const string PaymentsNotReady = "Dostawca tego agenta nie może jeszcze przyjmować płatności, więc nie można wykupić tego pakietu. Spróbuj z innym agentem albo wróć za jakiś czas.";
        public const string Generic = "Nie udało się wykupić dostępu. Sprawdź dane i spróbuj ponownie.";
        public const string BadTarget = "Nie udało się przejść do płatności. Spróbuj ponownie: nie zostaniesz obciążony dwa razy.";
    }

    public static class Assist
    {
        public const string Ask = "Pomóż mi opisać zastosowanie", Note = "Propozycja to tylko podpowiedź: możesz ją zmienić i nic nie zostanie dodane bez Twojego kliknięcia.";
        public const string NeedsText = "Najpierw opisz zastosowanie w co najmniej 20 znakach, wtedy asystent będzie mógł pomóc.";
        public const string Proposal = "Propozycja opisu (możesz ją edytować)", Questions = "Warto jeszcze opisać", Title = "Propozycja asystenta", Category = "Sugerowana kategoria: {0}", CategoryLabel = "Sugerowana kategoria:";
        public const string Apply = "Dodaj propozycję do opisu", Applied = "Propozycja została dodana do opisu.";
        public const string Unavailable = "Asystent jest chwilowo niedostępny. Opisz zastosowanie samodzielnie i wykup dostęp.";
        public const string RateLimited = "Zbyt wiele próśb do asystenta. Spróbuj za chwilę albo opisz zastosowanie samodzielnie.";
        public const string Failed = "Asystent nie mógł przygotować propozycji. Opisz zastosowanie samodzielnie.";
    }

    public static class Form
    {
        public const string Pay = "Zapłać {0}";
    }

    public static class Checkout
    {
        public const string Title = "Płatność (tryb demonstracyjny)", MetaTitle = "Płatność (tryb demonstracyjny) | Gabinety Tech";
        public const string Demo = "To tryb demonstracyjny: płatność jest symulowana i żadne pieniądze nie są pobierane.";
        public const string Succeed = "Symuluj udaną płatność", Fail = "Symuluj nieudaną płatność";
        public const string Hold = "Zakup jest wstrzymany jeszcze przez", HoldLabel = "Czas na płatność";
        public const string AnnounceFive = "Zostało 5 minut na płatność.", AnnounceOne = "Została 1 minuta na płatność.", Expired = "Czas na płatność minął. Wykup pakiet ponownie.";
        public const string Failed = "Płatność nie powiodła się. Dostęp nie został aktywowany.";
        public const string NotMock = "Symulacja płatności jest wyłączona w tym środowisku.", Error = "Symulacja nie powiodła się. Spróbuj ponownie.";
        public const string Closed = "Ten zakup nie oczekuje już na płatność.", ToBooking = "Zobacz dostęp", ToProfile = "Wróć do profilu", ToSearch = "Wróć do wyszukiwarki";
        public const string LoadFailed = "Nie udało się wczytać zakupu. Odśwież stronę.", Refresh = "Odśwież stronę";
        public const string Declined = "Płatność nie została dokończona.";
    }

    public static class Confirmation
    {
        public const string Title = "Dostęp", MetaTitle = "Dostęp | Gabinety Tech";
        /// <summary>The h1 of an active package; the other states keep <see cref="Title"/> so no page claims success it does not have.</summary>
        public const string ConfirmedTitle = "Dostęp aktywny";
        public const string Confirmed = "Dostęp aktywny. Dziękujemy!", Pending = "Czekamy na potwierdzenie płatności.", Other = "Ten dostęp nie jest aktywny.";
        public const string Refresh = "Odśwież", View = "Zobacz dostęp", ToSearch = "Wróć do wyszukiwarki";
        public const string State = "Stan", ValidUntil = "Ważny do";
        /// <summary>The "Co dalej" list under the summary of an active package.</summary>
        public const string NextTitle = "Co dalej", NextEmail = "Wyślemy e-mail ze szczegółami dostępu.";
        public const string NextCancel = "Do pierwszego użycia możesz zrezygnować w zakładce Moje dostępy. Zasady zwrotu znajdziesz w", NextCancelLink = "cenniku";
        public const string CreateKey = "Utwórz klucz API", FirstTask = "Zleć pierwsze zadanie";
        public const string ConnectTitle = "Jak się połączyć", ConnectBaseUrl = "Adres bazowy API", ConnectModel = "Nazwa modelu", ConnectHint = "Klucz API utworzysz w zakładce Klucze API; pokażemy go tylko raz.";
    }
}
