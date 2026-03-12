using OpenQA.Selenium;
using System;
using System.Threading;

partial class Program
{
    private static readonly Random HumanizationRandom = new();
    private static readonly object HumanizationRandomLock = new();
    private static readonly string[] EasyApplyCollectionEntryUrlVariants =
    {
        LinkedInEasyApplyCollectionUrl,
        "https://www.linkedin.com/jobs/collections/easy-apply/?discover=recommended&discoveryOrigin=JOBS_HOME_JYMBII",
        "https://www.linkedin.com/jobs/collections/easy-apply/?discoveryOrigin=JOBS_HOME_JYMBII&discover=recommended&start=0"
    };

    private static int InteractionDelayMinMs = 800;
    private static int InteractionDelayMaxMs = 2500;
    private static int BetweenApplicationsDelayMinMs = 5000;
    private static int BetweenApplicationsDelayMaxMs = 15000;
    private static int PaginationDelayMinMs = 1200;
    private static int PaginationDelayMaxMs = 2800;
    private static int CollectionEntryScrollMinPx = 120;
    private static int CollectionEntryScrollMaxPx = 720;
    private static TimeSpan? ActiveHoursStartLocal;
    private static TimeSpan? ActiveHoursEndLocal;

    private static void ConfigureHumanization(
        int interactionDelayMinMs,
        int interactionDelayMaxMs,
        int betweenApplicationsDelayMinMs,
        int betweenApplicationsDelayMaxMs,
        int paginationDelayMinMs,
        int paginationDelayMaxMs,
        TimeSpan? activeHoursStart,
        TimeSpan? activeHoursEnd)
    {
        InteractionDelayMinMs = Math.Max(150, interactionDelayMinMs);
        InteractionDelayMaxMs = Math.Max(InteractionDelayMinMs, interactionDelayMaxMs);
        BetweenApplicationsDelayMinMs = Math.Max(500, betweenApplicationsDelayMinMs);
        BetweenApplicationsDelayMaxMs = Math.Max(BetweenApplicationsDelayMinMs, betweenApplicationsDelayMaxMs);
        PaginationDelayMinMs = Math.Max(250, paginationDelayMinMs);
        PaginationDelayMaxMs = Math.Max(PaginationDelayMinMs, paginationDelayMaxMs);
        CollectionEntryScrollMaxPx = Math.Max(CollectionEntryScrollMinPx, CollectionEntryScrollMaxPx);

        if (activeHoursStart.HasValue && activeHoursEnd.HasValue && activeHoursStart.Value != activeHoursEnd.Value)
        {
            ActiveHoursStartLocal = activeHoursStart.Value;
            ActiveHoursEndLocal = activeHoursEnd.Value;
            return;
        }

        ActiveHoursStartLocal = null;
        ActiveHoursEndLocal = null;
    }

    private static int NextRandomInt(int minValue, int maxValue)
    {
        if (minValue > maxValue)
        {
            (minValue, maxValue) = (maxValue, minValue);
        }

        lock (HumanizationRandomLock)
        {
            return HumanizationRandom.Next(minValue, maxValue + 1);
        }
    }

    private static void SleepRandomDelay(int minMs, int maxMs, string? label = null, bool log = false)
    {
        var delayMs = NextRandomInt(minMs, maxMs);
        if (log && !string.IsNullOrWhiteSpace(label))
        {
            Console.WriteLine($"[HUMAN] {label}: aguardando {delayMs} ms.");
        }

        Thread.Sleep(delayMs);
    }

    private static void PauseBeforeClick()
    {
        SleepRandomDelay(160, 360);
    }

    private static void PauseBetweenFlowSteps(string? label = null, bool log = false)
    {
        SleepRandomDelay(InteractionDelayMinMs, InteractionDelayMaxMs, label, log);
    }

    private static void PauseAfterPaginationAdvance()
    {
        SleepRandomDelay(PaginationDelayMinMs, PaginationDelayMaxMs);
    }

    private static void PauseBetweenApplications(string link, bool flowCompleted)
    {
        var normalizedLink = NormalizeLinkedInJobLink(link) ?? link;
        var description = flowCompleted
            ? $"intervalo apos candidatura para {normalizedLink}"
            : $"intervalo apos tentativa nao concluida para {normalizedLink}";

        SleepRandomDelay(BetweenApplicationsDelayMinMs, BetweenApplicationsDelayMaxMs, description, log: true);
    }

    private static string GetEasyApplyCollectionEntryUrlForCycle()
    {
        var variantIndex = NextRandomInt(0, EasyApplyCollectionEntryUrlVariants.Length - 1);
        return EasyApplyCollectionEntryUrlVariants[variantIndex];
    }

    private static void HumanizeCollectionEntry(IWebDriver driver)
    {
        try
        {
            var scrollTarget = NextRandomInt(CollectionEntryScrollMinPx, CollectionEntryScrollMaxPx);
            ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollTo({ top: arguments[0], behavior: 'smooth' });", scrollTarget);
            SleepRandomDelay(250, Math.Min(900, InteractionDelayMaxMs));
        }
        catch
        {
            // Segue sem scroll auxiliar.
        }
    }

    private static void TryScrollElementIntoViewHumanized(IWebDriver driver, IWebElement element)
    {
        try
        {
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({ behavior: 'smooth', block: 'center', inline: 'nearest' });", element);
            SleepRandomDelay(180, 420);
        }
        catch
        {
            try
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({ block: 'center' });", element);
            }
            catch
            {
                // Elemento pode ter ficado stale; o clique tentará do mesmo jeito.
            }
        }
    }

    private static string DescribeActiveHoursWindow()
    {
        if (!ActiveHoursStartLocal.HasValue || !ActiveHoursEndLocal.HasValue)
        {
            return "desativada";
        }

        return $"{ActiveHoursStartLocal.Value:hh\\:mm}-{ActiveHoursEndLocal.Value:hh\\:mm}";
    }

    private static void WaitUntilWithinActiveHoursIfNeeded()
    {
        if (!ActiveHoursStartLocal.HasValue || !ActiveHoursEndLocal.HasValue)
        {
            return;
        }

        var now = DateTime.Now;
        if (IsWithinActiveHours(now.TimeOfDay, ActiveHoursStartLocal.Value, ActiveHoursEndLocal.Value))
        {
            return;
        }

        var nextStart = GetNextActiveWindowStart(now, ActiveHoursStartLocal.Value);
        var waitTime = nextStart - now;
        if (waitTime <= TimeSpan.Zero)
        {
            return;
        }

        Console.WriteLine($"Fora da janela ativa {DescribeActiveHoursWindow()}. Aguardando ate {nextStart:dd/MM/yyyy HH:mm} ({Math.Ceiling(waitTime.TotalMinutes)} min).");
        SleepWithRuntimeHeartbeat(waitTime, "waiting", $"Fora da janela ativa {DescribeActiveHoursWindow()}. Aguardando proxima janela.");
    }

    private static bool IsWithinActiveHours(TimeSpan currentTime, TimeSpan startTime, TimeSpan endTime)
    {
        if (startTime == endTime)
        {
            return true;
        }

        if (startTime < endTime)
        {
            return currentTime >= startTime && currentTime < endTime;
        }

        return currentTime >= startTime || currentTime < endTime;
    }

    private static DateTime GetNextActiveWindowStart(DateTime now, TimeSpan startTime)
    {
        var todayStart = now.Date.Add(startTime);
        return todayStart > now ? todayStart : todayStart.AddDays(1);
    }
}