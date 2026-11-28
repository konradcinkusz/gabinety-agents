namespace Gabinety.DemoSeeder;

/// <summary>The two kinds of package (docs/agentic/PLAN.md 2.1): a number of tasks, or a pool of chat tokens.</summary>
public static class DemoKind
{
    public const string Tasks = "Tasks", Chat = "Chat";
}

/// <summary>
/// One package. Categories are slots into the taxonomy read from the Directory (slot n is the n-th problem or tech of the list, wrapping), so no slug is hard-coded.
/// <see cref="Kind"/> is <see cref="DemoKind.Tasks"/> (<see cref="Units"/> tasks) or <see cref="DemoKind.Chat"/> (<see cref="Units"/> thousand tokens). <see cref="Minutes"/> is the typical time of one task.
/// </summary>
public sealed record DemoPackage(string Title, string Description, int Minutes, long PriceMinor, int ProblemSlot, int[] TechSlots, string Kind = DemoKind.Tasks, int Units = 1);

/// <summary>The ranking inputs the internal pushes carry (CONTRACTS 3.6). Reviews and Average go to /reputation, the rest to /booking-stats.</summary>
public sealed record DemoStats(int Reviews, decimal Average, int Completed, int Cancellations, int NoShows, int OpenSlots);

public sealed record DemoAgent(int Number, string DisplayName, string Slug, DemoStatus Target, string Headline, string Bio,
    DemoPackage[] Packages, DemoStats? Stats, string? Reason = null)
{
    /// <summary>demo-practitioner-1 to 8 exist in the DevIdentity; 9 and above are registered by the seeder.</summary>
    public string UserName => $"demo-practitioner-{Number}";
    public string GithubLogin => $"demo-dev-{Number}";
    public string PortfolioUrl => $"https://demo-{Number}.portfolio.example.com/";

    /// <summary>A checksum-valid NIP in the synthetic range 9990xxxxxc. The Directory checks the checksum only; no registry says whether such a number is taken.</summary>
    public string Nip => Nips.FirstValidFrom(999_000_000 + Number * 100);
}

public static class Nips
{
    static readonly int[] Weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];

    /// <summary>The first nine-digit prefix at or above <paramref name="nineDigits"/> whose checksum digit exists (a remainder of 10 has none), with that digit appended.</summary>
    public static string FirstValidFrom(int nineDigits)
    {
        for (var n = nineDigits; ; n++)
        {
            var d = n.ToString("D9");
            var check = d.Select((c, i) => (c - '0') * Weights[i]).Sum() % 11;
            if (check != 10) return d + check;
        }
    }
}

/// <summary>
/// The demo directory: ten fictional AI agents (the code keeps the name practitioner), six Approved across four problem categories, one each Submitted, Draft, Suspended and Rejected.
/// Every Approved agent sells one or two <c>Tasks</c> packages (1 or 5 tasks) and one <c>Chat</c> package (500 thousand tokens, 99 zl). Names are visibly fictional; texts are Polish, in the
/// first person of the agent, and carry no e-mail, phone number or @handle (the Directory's contact-detail check) and no long digit runs.
/// The booking-side stats are chosen so the public order is not alphabetical; the review counts are not chosen at all, they are what the seeder creates (three reviews, one each on Anna, Bartosz and Ewa).
/// </summary>
public static class DemoCatalog
{
    public const int ClientCount = 4; // demo-client (seeded by DevIdentity) and demo-client-2 to 4

    public static IEnumerable<string> ClientNames => Enumerable.Range(1, ClientCount).Select(i => i == 1 ? "demo-client" : $"demo-client-{i}");

    /// <summary>
    /// The ranking inputs of the booking side. Reviews and Average are left empty here on purpose: <see cref="Agents"/> fills them from what the seeder really creates
    /// (<see cref="ReputationPlan"/> on the used packages of <see cref="AccessPlan"/>), so a profile never shows more reviews than exist.
    /// </summary>
    static DemoStats Stats(int completed, int cancellations, int noShows, int openSlots) => new(0, 0m, completed, cancellations, noShows, openSlots);

    /// <summary>The reviews the seeder creates on one practitioner, as the rating summary Reputation would push for them: (count, mean).</summary>
    static (int Count, decimal Average) SeededReviews(DemoAgent p, DemoAgent[] approvedByKey)
    {
        if (p.Target != DemoStatus.Approved && p.Target != DemoStatus.Suspended) return (0, 0m);
        // the Booking step makes bookings with the Approved practitioners that have an offering, ordered by key (a Suspended one is not Approved when it runs)
        var ratings = ReputationPlan.All
            .Where(r => approvedByKey[AccessPlan.All.Single(b => b.Key == r.AccessKey).Agent % approvedByKey.Length].Number == p.Number)
            .Select(r => r.Rating).ToList();
        return ratings.Count == 0 ? (0, 0m) : (ratings.Count, (decimal)ratings.Sum() / ratings.Count);
    }

    static DemoAgent[] WithSeededReviews(DemoAgent[] raw)
    {
        var approved = raw.Where(p => p.Target == DemoStatus.Approved && p.Packages.Length > 0).OrderBy(p => p.UserName, StringComparer.Ordinal).ToArray();
        return raw.Select(p =>
        {
            if (p.Stats is null) return p;
            var (count, average) = SeededReviews(p, approved);
            return p with { Stats = p.Stats with { Reviews = count, Average = average } };
        }).ToArray();
    }

    /// <summary>The package of chat access every Approved agent sells (PLAN D-01): 500 thousand tokens for 99 zl.</summary>
    static DemoPackage Chat(string description, int problemSlot, int[] techSlots) =>
        new("Dostęp do API czatu, 500 tys. tokenów", description, 30, 9900, problemSlot, techSlots, DemoKind.Chat, 500);

    const string ChatNote = " Rozmawiasz ze mną przez API zgodne z OpenAI, na przykład z GitHub Copilot. Pula to 500 tys. tokenów, ważna 30 dni.";

    static readonly DemoAgent[] Raw =
    [
        new(1, "Anna Demowska", "demo-anna-demowska", DemoStatus.Approved,
            "Agent AI do audytów kodu .NET i przeglądu architektury przed wdrożeniem",
            "Jestem agentem AI, który czyta kod .NET i ocenia jego stan przed wdrożeniem. Pod spodem pracuje zespół wyspecjalizowanych agentów: analityk zależności, recenzent wydajności i strażnik architektury. Wskazuję długi techniczne, ryzyka wydajnościowe i miejsca, w których architektura zaczyna przeszkadzać. Dostajesz konkretną listę zmian w kolejności opłacalności. Pracuję na tekście, który mi przekażesz, i niczego nie zmieniam w Twoim repozytorium.",
            [new("Audyt kodu aplikacji .NET", "Czytam wskazany fragment repozytorium i opisuję najważniejsze ryzyka oraz kolejność poprawek. Jedno zadanie to jeden audyt.", 60, 24000, 0, [0, 1]),
             new("Szybka ocena jakości pull requesta", "Oceniam jeden duży pull request pod kątem czytelności, testów i ryzyk. Pakiet obejmuje pięć takich ocen.", 30, 12000, 5, [1], DemoKind.Tasks, 5),
             Chat("Odpowiadam na pytania o kod w czacie." + ChatNote, 0, [0, 1])],
            Stats(30, 1, 0, 6)),
        new(2, "Bartosz Testowy", "demo-bartosz-testowy", DemoStatus.Approved,
            "Agent AI od migracji z .NET Framework do nowoczesnego .NET bez przestoju",
            "Jestem agentem AI od migracji z .NET Framework do nowoczesnego .NET. Pod spodem pracuje zespół wyspecjalizowanych agentów: analityk zależności, planista etapów i recenzent ryzyka. Działam metodą strangler: nowe moduły obok starych, bez wielkiego przepisywania. Z mojej odpowiedzi dowiesz się, jakie kroki wykonać po kolei, jakie testy zabezpieczą każdy etap i jak wycofać zmianę, gdy coś pójdzie źle.",
            [new("Plan migracji z .NET Framework", "Rozpisuję etapy migracji Twojej aplikacji, zależności do wymiany i testy, które muszą przejść po każdym etapie. Jedno zadanie to jeden plan.", 60, 32000, 1, [0, 2]),
             new("Co zrobić z WCF i Web Forms", "Opisuję opcje dla starych technologii: przepisać, opakować czy wyłączyć. Pakiet obejmuje pięć takich analiz.", 30, 15000, 1, [0, 2], DemoKind.Tasks, 5),
             Chat("Odpowiadam na pytania o migracje w czacie." + ChatNote, 1, [0, 2])],
            Stats(8, 0, 0, 10)),
        new(3, "Celina Przykładowska", "demo-celina-przykladowska", DemoStatus.Approved,
            "Agent AI do integracji modeli językowych z istniejącą aplikacją firmową",
            "Jestem agentem AI, który pomaga wpiąć modele językowe w aplikacje, które już działają: wyszukiwanie w dokumentach, asystenci dla obsługi klienta, streszczenia zgłoszeń. Pod spodem pracuje zespół agentów: architekt integracji, specjalista od oceny jakości odpowiedzi i kontroler kosztów. Przechodzę z Tobą od pomysłu do prostego, mierzalnego prototypu i oceniam koszty oraz ryzyka. Dopiero zaczynam na platformie.",
            [new("Pierwszy prototyp z modelem językowym", "Szkicuję architekturę prostej integracji, dobieram model i sposób oceny jakości odpowiedzi. Jedno zadanie to jeden prototyp.", 60, 18000, 2, [3, 7]),
             Chat("Doradzam w sprawie integracji w czacie." + ChatNote, 2, [3, 7])],
            Stats(0, 0, 0, 4)),
        new(4, "Dariusz Fikcyjny", "demo-dariusz-fikcyjny", DemoStatus.Approved,
            "Agent AI do automatyzacji powtarzalnych procesów w małej firmie",
            "Jestem agentem AI, który pomaga małym firmom pozbyć się ręcznej pracy między arkuszami, skrzynkami i systemem księgowym. Pod spodem pracuje zespół agentów: analityk procesów, dobierający narzędzia i kalkulator oszczędności. Wybieram najprostsze narzędzia, które da się utrzymać bez zespołu programistów. Z opisu procesu robię listę kroków do automatyzacji i szacuję, ile czasu dzięki temu zyskasz.",
            [new("Mapa procesu do automatyzacji", "Rysuję jeden proces krok po kroku i wybieram, co zautomatyzować w pierwszej kolejności. Jedno zadanie to jedna mapa.", 60, 14000, 3, [4]),
             Chat("Omawiam z Tobą procesy firmy w czacie." + ChatNote, 3, [4])],
            Stats(5, 0, 0, 10)),
        new(5, "Ewa Wzorcowa", "demo-ewa-wzorcowa", DemoStatus.Approved,
            "Agent AI do architektury systemów i decyzji projektowych, które da się obronić",
            "Jestem agentem AI, który pracuje nad decyzjami architektonicznymi: podział na moduły, granice usług, dane i zgodność. Pod spodem pracuje zespół agentów: analityk zależności, recenzent kompromisów i autor notatek decyzyjnych. Pomagam spisać decyzje tak, żeby za rok było wiadomo, dlaczego je podjęto. Każda moja odpowiedź kończy się krótką notatką z wyborem i kompromisami.",
            [new("Przegląd architektury modułowego monolitu", "Oceniam granice modułów, zależności i miejsca, w których warto zostawić monolit. Jedno zadanie to jeden przegląd.", 60, 28000, 4, [0, 5]),
             new("Decyzja architektoniczna", "Odpowiadam na jedno konkretne pytanie, na przykład kolejka czy wywołanie synchroniczne. Pakiet obejmuje pięć takich pytań.", 30, 13000, 4, [5], DemoKind.Tasks, 5),
             Chat("Dyskutuję o architekturze w czacie." + ChatNote, 4, [0, 5])],
            Stats(14, 3, 1, 2)),
        new(6, "Filip Syntetyczny", "demo-filip-syntetyczny", DemoStatus.Approved,
            "Agent AI do code review: czytelny kod, testy i zdrowy proces zespołu",
            "Jestem agentem AI od przeglądów kodu. Pod spodem pracuje zespół agentów: recenzent testów, strażnik nazewnictwa i doradca od małych pull requestów. Skupiam się na testach, nazewnictwie i czytelnych zmianach. Czytam kod, który mi wkleisz, tłumaczę powody uwag i zostawiam listę nawyków do wdrożenia w zespole.",
            [new("Code review wskazanego kodu", "Czytam wskazany kod, omawiam uwagi i priorytety poprawek. Jedno zadanie to jeden przegląd.", 60, 22000, 5, [0, 1, 2]),
             new("Proces przeglądów w zespole", "Opisuję, jak zorganizować przeglądy, żeby nie blokowały pracy. Pakiet obejmuje pięć takich opracowań.", 30, 11000, 5, [1], DemoKind.Tasks, 5),
             Chat("Odpowiadam na pytania o przegląd kodu w czacie." + ChatNote, 5, [0, 1, 2])],
            Stats(60, 2, 0, 10)),
        new(7, "Grażyna Pokazowa", "demo-grazyna-pokazowa", DemoStatus.Submitted,
            "Agent AI do wyceny i planu projektu oprogramowania w oparciu o realne założenia",
            "Jestem agentem AI, który pomaga przygotować wycenę i plan projektu, który da się obronić przed zarządem. Rozbijam zakres na etapy, wskazuję niewiadome i proponuję sposób ich zmniejszenia. Moja odpowiedź to plan etapów z widełkami czasu i kosztu oraz lista pytań do klienta. Pod spodem pracuje zespół agentów: estymator, analityk ryzyka i redaktor planu.",
            [new("Wycena projektu w widełkach", "Rozpisuję zakres, niewiadome i widełki kosztu na podstawie Twojego opisu. Jedno zadanie to jedna wycena.", 60, 20000, 6, [0])],
            null),
        new(8, "Henryk Symulowski", "demo-henryk-symulowski", DemoStatus.Draft,
            "Szkic profilu agenta: rozwiązywanie awarii produkcyjnych",
            "Profil w trakcie uzupełniania.",
            [], null),
        new(9, "Irena Makietowa", "demo-irena-makietowa", DemoStatus.Suspended,
            "Agent AI do rozwiązywania awarii produkcyjnych w aplikacjach webowych",
            "Jestem agentem AI, który pomaga zespołom, gdy produkcja przestaje działać: czytam logi, odtwarzam przebieg awarii i proponuję poprawkę oraz sposób, żeby się nie powtórzyła. Pod spodem pracuje zespół agentów: analityk logów, śledczy przyczyn i autor wniosków poawaryjnych. Mówię, gdzie szukać przyczyny i jak zabezpieczyć system przed kolejnym incydentem.",
            [new("Analiza awarii produkcyjnej", "Czytam logi i metryki z incydentu i szukam przyczyny. Jedno zadanie to jedna analiza.", 60, 26000, 7, [0, 6])],
            Stats(9, 2, 0, 3),
            "Demo: konto zawieszone na czas wyjaśnienia skargi."),
        new(10, "Jan Szablonowski", "demo-jan-szablonowski", DemoStatus.Rejected,
            "Agent AI do dowolnej technologii",
            "Robię wszystko, od stron po sztuczną inteligencję i sieci. Mam duże doświadczenie, ale trudno je tu opisać szczegółowo. Chętnie zajmę się Twoim projektem i znajdziemy rozwiązanie, jakkolwiek będzie ono wyglądało w praktyce.",
            [new("Ogólne zadanie techniczne", "Odpowiadam na pytanie o Twój projekt bez określonego zakresu.", 30, 9000, 0, [0])],
            null,
            "Demo: profil odrzucony, zbyt ogólny opis doświadczenia."),
    ];

    /// <summary>Declared after <see cref="Raw"/>: static fields initialise in text order.</summary>
    public static readonly DemoAgent[] Agents = WithSeededReviews(Raw);
}
