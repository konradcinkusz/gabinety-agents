namespace Gabinety.Web.Text;

/// <summary>The Polish text of the client area (docs/agentic/PLAN.md C-03): my accesses, the access detail with its actions (cancel, complaint, review), the tasks of a package and the API keys.</summary>
public static class BookingsText
{
    public const string DetailTitle = "Szczegóły dostępu", DetailMetaTitle = "Szczegóły dostępu | Gabinety Tech";
    public const string TabsLabel = "Dostępy jako", Details = "Szczegóły", Back = "Wróć do listy dostępów", Review = "Wystaw opinię";
    public const string Loading = "Ładowanie dostępów…", NotFound = "Nie znaleziono takiego dostępu.";
    public const string EmptyTitle = "Brak dostępów", EmptyClient = "Gdy wykupisz dostęp do agenta, zobaczysz go tutaj.", EmptyPractitioner = "Gdy klient wykupi pakiet Twojego agenta, zobaczysz go tutaj.", EmptyFiltered = "Brak dostępów o tym statusie.";
    public const string FindPractitioner = "Znajdź agenta", ShowAll = "Pokaż wszystkie statusy", Next = "Pokaż kolejne dostępy";
    public const string ReloadHint = "Odśwież stronę", Retry = "Spróbuj ponownie";
    public const string PartyPractitioner = "Agent", PartyClient = "Klient";
    public const string UsageLabel = "Wykorzystanie pakietu";

    public static class Detail
    {
        public const string Units = "Pakiet", ValidUntil = "Ważny do", Usage = "Wykorzystanie", Status = "Status", Price = "Cena", Payment = "Płatność";
        public const string Refunded = "Zwrócono: {0}", Fee = "Prowizja", Payout = "Wypłata dla dostawcy";
        public const string Brief = "Zastosowanie (klient)", BriefOwn = "Twoje zastosowanie";
        public const string ActiveLine = "Dostęp aktywny do {0}.", CompletedLine = "Pakiet zakończony.", RefundLine = "Zwrot za ten dostęp: {0}.", NoRefundLine = "Rezygnacja bez zwrotu.", Rebook = "Wykup ponownie";
    }

    public static class Payment
    {
        public const string Created = "Płatność rozpoczęta", Succeeded = "Opłacona", Failed = "Płatność nieudana", Refunded = "Zwrócona w całości", PartiallyRefunded = "Zwrócona częściowo", Unknown = "Stan płatności nieznany";
    }

    public static class Actions
    {
        public const string Cancel = "Zrezygnuj", Dispute = "Zgłoś reklamację";
    }

    public static class Cancel
    {
        public const string Title = "Czy na pewno zrezygnować z pakietu?";
        /// <summary>The rule of docs/agentic/PLAN.md 2.5, said when the access does not carry its own outcome (F-11): the page never computes the outcome itself.</summary>
        public const string ClientRule = "Do pierwszego użycia rezygnacja oznacza pełny zwrot ceny.";
        public const string PractitionerRule = "Jako dostawca wycofujesz dostęp zawsze z pełnym zwrotem: klient dostanie z powrotem 100% ceny, a Ty nie otrzymasz wypłaty za ten pakiet.";
        public const string OutcomeFull = "Otrzymasz zwrot {0}.", OutcomeNone = "Nie otrzymasz zwrotu.";
        public const string OutcomePartial = "Otrzymasz zwrot {0} z {1}.";
        public const string PractitionerOutcome = "Klient dostanie zwrot {0}, a Ty nie otrzymasz wypłaty za ten pakiet.";
        public const string ReasonLabel = "Powód (opcjonalnie)", ReasonHint = "Najwyżej 500 znaków.", Confirm = "Tak, zrezygnuj", Keep = "Nie rezygnuj", Done = "Zrezygnowano z pakietu.";
        public const string ReasonTooLong = "Powód może mieć najwyżej 500 znaków.";
    }

    public static class Dispute
    {
        public const string Title = "Zgłoszenie reklamacji", Intro = "Wypłata dla dostawcy zostanie wstrzymana do rozstrzygnięcia przez administratora.", KindLabel = "Rodzaj reklamacji";
        public const string NotAsDescribed = "Agent działał inaczej, niż opisano", Other = "Inny powód";
        public const string TextLabel = "Opis reklamacji", TextHint = "Od 10 do 1000 znaków. Opisz, co się stało; administrator zobaczy ten opis.";
        public const string TooShort = "Opis musi mieć co najmniej 10 znaków.", TooLong = "Opis może mieć najwyżej 1000 znaków.", KindInvalid = "Wybierz rodzaj reklamacji.";
        public const string Submit = "Wyślij zgłoszenie", Close = "Zamknij", Done = "Reklamacja została zgłoszona. Wypłata dla dostawcy jest wstrzymana do rozstrzygnięcia.";
        public const string Counter = "{0} / 1000";
    }

    /// <summary>The tasks of a package (<c>Tasks</c> kind): the form, the list with state and result. The result is the agent's text: it is rendered as plain text, never as markup.</summary>
    public static class Tasks
    {
        public const string Title = "Zadania", FormTitle = "Zleć zadanie", InputLabel = "Treść zadania", InputHint = "Od 20 do 20000 znaków. Opisz, co agent ma zrobić.", Submit = "Zleć zadanie", Counter = "{0} / 20000";
        public const string TooShort = "Treść zadania musi mieć co najmniej 20 znaków.", TooLong = "Treść zadania może mieć najwyżej 20000 znaków.";
        public const string Done = "Zadanie zostało zlecone. Wynik pojawi się poniżej.";
        public const string ListTitle = "Twoje zadania", Empty = "Nie zleciłeś jeszcze żadnego zadania.", Result = "Wynik", NoResult = "Brak wyniku.";
        public const string Created = "Zlecone: {0}", Completed = "zakończone: {0}", Refresh = "Odśwież listę", Pending = "Zadanie jest w toku.";
        public const string Unavailable = "Nie udało się wczytać zadań. Spróbuj ponownie za chwilę.", NotActive = "Zadania można zlecać tylko w aktywnym pakiecie zadań, który nie jest wyczerpany.";
        public const string Exhausted = "Ten pakiet jest wyczerpany albo nieaktywny.";
        public const string ListLabel = "Lista zadań";

        /// <summary>The Polish label of a task status; an unknown value is shown as such, never as the raw wire value.</summary>
        public static readonly IReadOnlyDictionary<string, string> Statuses = new Dictionary<string, string>
        {
            ["queued"] = "W kolejce",
            ["running"] = "W toku",
            ["succeeded"] = "Gotowe",
            ["failed"] = "Nieudane (jednostka wraca do pakietu)",
        };
        public const string UnknownStatus = "Status nieznany";
    }

    public static class Errors
    {
        public const string InvalidTransition = "To nie jest już możliwe: stan dostępu się zmienił. Odśwież stronę.", Refresh = "Odśwież";
        public const string Unavailable = "Usługa dostępów jest chwilowo niedostępna. Spróbuj ponownie za chwilę.", Validation = "Sprawdź wypełnione pola i spróbuj ponownie.";
        public const string Forbidden = "Nie masz dostępu do tego zasobu.", RateLimited = "Zbyt wiele prób. Odczekaj chwilę i spróbuj ponownie.";
        public const string Network = "Nie udało się połączyć z usługą. Spróbuj ponownie.", Generic = "Coś poszło nie tak. Spróbuj ponownie.";
        public const string SignedOut = "Sesja wygasła. Zaloguj się ponownie.", SignIn = "Zaloguj się";
        public const string QuotaExhausted = "Ten pakiet jest wyczerpany.";
    }

    /// <summary>The API keys page (<c>/moje/klucze</c>). The plain key is shown once, right after it is created; the list shows the prefix only.</summary>
    public static class Keys
    {
        public const string Title = "Klucze API", MetaTitle = "Klucze API | Gabinety Tech";
        public const string Intro = "Jeden klucz działa dla wszystkich agentów, do których masz aktywny pakiet. Możesz mieć do 5 aktywnych kluczy.";
        public const string ListTitle = "Twoje klucze", ListLabel = "Lista kluczy API", Empty = "Nie masz jeszcze żadnego klucza.";
        public const string ColName = "Nazwa", ColPrefix = "Prefiks", ColCreated = "Utworzono", ColLastUsed = "Ostatnio użyto", NeverUsed = "jeszcze nie użyto";
        public const string CreateTitle = "Utwórz klucz", NameLabel = "Nazwa klucza", NameHint = "Do 60 znaków, na przykład „Moje IDE”.", Create = "Utwórz klucz";
        public const string NameRequired = "Podaj nazwę klucza, najwyżej 60 znaków.", LimitReached = "Masz już 5 aktywnych kluczy. Unieważnij jeden, aby utworzyć nowy.";
        public const string NewTitle = "Twój nowy klucz", ShownOnce = "Skopiuj klucz teraz. Pokazujemy go tylko raz i nie da się go odzyskać. Kto go zna, korzysta z Twoich pakietów.";
        public const string Copy = "Kopiuj klucz", Copied = "Skopiowano", KeyLabel = "Klucz API";
        public const string Revoke = "Unieważnij", RevokeTitle = "Unieważnić klucz?", RevokeBody = "Aplikacje używające klucza „{0}” przestaną działać. Tego nie można cofnąć.", RevokeConfirm = "Tak, unieważnij", RevokeKeep = "Nie unieważniaj";
        public const string Revoked = "Klucz został unieważniony.", Counter = "{0} / 60";
        public const string Unavailable = "Nie udało się wczytać kluczy. Spróbuj ponownie za chwilę.";
    }
}
