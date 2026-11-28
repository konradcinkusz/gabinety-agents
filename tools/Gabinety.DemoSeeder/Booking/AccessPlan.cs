using System.Security.Cryptography;
using System.Text;

namespace Gabinety.DemoSeeder;

/// <summary>The state a demo booking is brought to. Names are the API's booking statuses.</summary>
public enum AccessTarget { Confirmed, PendingPayment, Expired, CancelledByClient, Completed, Disputed, Resolved }

/// <summary>Which package of the agent a demo booking buys: its single-task package (<c>Tasks</c>, 1 unit), its task pack (<c>Tasks</c>, 5 units) or its <c>Chat</c> package.</summary>
public enum PackagePick { SingleTask, TaskPack, Chat }

/// <summary>
/// One demo booking: the purchase of one package. <see cref="Key"/> is its stable name (starts with demo-); it is written into the brief as <c>[key] ...</c>, which is how a later run finds the
/// booking again, and the Idempotency-Key derives from it. Practitioner (the agent) and client are indexes (wrapping) into the Approved agents and the demo clients.
/// <see cref="Tasks"/> is how many tasks are ordered through the client API (<c>POST /api/v1/bookings/{id}/tasks</c>), so the package really has <c>UnitsUsed = Tasks</c>; ordering the last unit of a
/// single-task package also makes the service's auto-complete job complete it (<see cref="AccessTarget.Completed"/>). There is no date: a package is valid for 30 days from the purchase.
/// </summary>
public sealed record DemoAccess(string Key, AccessTarget Target, int Agent, int Client, PackagePick Package = PackagePick.SingleTask, int Tasks = 0)
{
    // the text is picked by a stable sum (string.GetHashCode is randomised per process)
    static readonly string[] BriefTexts =
    [
        "Chcę sprawdzić, jak agent ocenia architekturę modułu płatności i kolejność refaktoryzacji.",
        "Potrzebuję przeglądu pull requesta i wskazówek do testów integracyjnych.",
        "Proszę o pomoc w planie migracji aplikacji do nowszej wersji .NET.",
        "Szukam wsparcia przy analizie wolnych zapytań w bazie danych.",
    ];

    static readonly string[] TaskTexts =
    [
        "Przeanalizuj opisany moduł rozliczeń i wskaż trzy największe ryzyka wraz z kolejnością poprawek.",
        "Oceń zakres opisanego pull requesta pod kątem czytelności i testów, podaj krótką listę uwag.",
        "Zaproponuj etapy migracji opisanej aplikacji do nowszej wersji .NET wraz z testami po każdym etapie.",
    ];

    public string BriefText => $"[{DemoNames.Require(Key)}] {BriefTexts[Stable(Key) % BriefTexts.Length]}";

    /// <summary>The text of the n-th task of this booking. Synthetic; the service never logs it and neither does the seeder.</summary>
    public string TaskText(int n) => TaskTexts[(Stable(Key) + n) % TaskTexts.Length];

    static int Stable(string s) => s.Aggregate(17, (h, c) => unchecked(h * 31 + c)) & 0x7fffffff;

    /// <summary>The Idempotency-Key of the create call: the first 16 bytes of SHA-256 over a fixed prefix and the demo key. The same demo booking always sends the same key.</summary>
    public Guid IdempotencyKey => new(SHA256.HashData(Encoding.UTF8.GetBytes("gabinety-demo-booking:" + Key))[..16]);
}

public static class AccessPlan
{
    /// <summary>
    /// Eleven purchases, one or more per state. No clock is moved: a package is Completed when its last unit is used (the auto-complete job does it), Disputed and Resolved
    /// need no time to pass (a claim is allowed on a Confirmed package too) and use a task pack, so the auto-complete job never has a reason to touch them while the claim is made (its read and write are not
    /// atomic on the InMemory database of the fake profile), and a cancellation before the first use is a full refund. Every package with <see cref="DemoAccess.Tasks"/> above 0 is used through the real task API,
    /// so Reputation may review it (a review needs <c>UnitsUsed &gt; 0</c>). <c>demo-client</c> (client 0) gets an unused task pack, a used one and a <c>Chat</c> package.
    /// </summary>
    public static readonly DemoAccess[] All =
    [
        new("demo-booking-confirmed-1", AccessTarget.Confirmed, 0, 0, PackagePick.TaskPack),
        new("demo-booking-confirmed-2", AccessTarget.Confirmed, 1, 0, PackagePick.TaskPack, Tasks: 1),
        new("demo-booking-chat-1", AccessTarget.Confirmed, 1, 0, PackagePick.Chat),
        new("demo-booking-cancelled-1", AccessTarget.CancelledByClient, 2, 0),
        new("demo-booking-expired-1", AccessTarget.Expired, 5, 2),
        new("demo-booking-completed-1", AccessTarget.Completed, 0, 1, Tasks: 1),
        new("demo-booking-completed-2", AccessTarget.Completed, 1, 2, Tasks: 1),
        new("demo-booking-completed-3", AccessTarget.Completed, 4, 0, Tasks: 1),
        new("demo-booking-disputed-1", AccessTarget.Disputed, 5, 1, PackagePick.TaskPack, Tasks: 1),
        new("demo-booking-resolved-1", AccessTarget.Resolved, 4, 3, PackagePick.TaskPack, Tasks: 1),
        new("demo-booking-pending-1", AccessTarget.PendingPayment, 5, 1),
    ];
}
