using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Threading;

partial class Program
{
    private static (List<string> RawLines, List<(string Titulo, string Empresa, string Localizacao, string Link)> StructuredJobs) ExtractSimplifiedJobs(IReadOnlyCollection<IWebElement> jobCards)
    {
        var rawLines = new List<string>();
        var structuredJobs = new List<(string Titulo, string Empresa, string Localizacao, string Link)>();

        foreach (var card in jobCards)
        {
            try
            {
                if (!HasSimplifiedApplication(card))
                {
                    continue;
                }

                var title = GetTextBySelectors(card,
                    "a.job-card-container__link strong",
                    "a.job-card-list__title",
                    "a.job-card-container__link span[aria-hidden='true']");

                var company = GetTextBySelectors(card,
                    ".artdeco-entity-lockup__subtitle",
                    "a.job-card-container__company-name",
                    ".job-card-container__primary-description");

                var location = GetTextBySelectors(card,
                    ".job-card-container__metadata-wrapper span",
                    "span.job-card-container__metadata-item",
                    ".job-card-container__metadata-item");

                var link = GetAttributeBySelectors(card, "href",
                    "a.job-card-container__link",
                    "a.job-card-list__title");

                if (IsBudgetExhaustedJobLink(link))
                {
                    Console.WriteLine("Vaga ignorada na coleta por eBP=BUDGET_EXHAUSTED_JOB.");
                    continue;
                }

                var normalizedLink = NormalizeLinkedInJobLink(link);
                if (string.IsNullOrWhiteSpace(normalizedLink))
                {
                    continue;
                }

                if (IsIgnoredJobLinkInCurrentRun(normalizedLink))
                {
                    Console.WriteLine("Card ignorado na coleta por estar na lista de links ignorados desta execução.");
                    continue;
                }

                var jobLine = $"{title} | {company} | {location} | {normalizedLink}";
                Console.WriteLine($"Vaga encontrada: {jobLine}");

                rawLines.Add(jobLine);
                structuredJobs.Add((title, company, location, normalizedLink));
            }
            catch (NoSuchElementException)
            {
                continue;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro inesperado: {ex.Message}");
            }
        }

        return (rawLines, structuredJobs);
    }

    private static bool TryCollectJobsFromCurrentResults(
        IWebDriver driver,
        WebDriverWait wait,
        List<string> allJobsLines,
        List<(string Titulo, string Empresa, string Localizacao, string Link)> allJobsData,
        int maxPagesPerCycle,
        string sourceLabel)
    {
        if (!WaitForJobsResults(driver))
        {
            Console.WriteLine($"Nao foi possivel carregar a lista de vagas para {sourceLabel} (timeout).");
            return false;
        }

        HumanizeCollectionEntry(driver);

        var initialJobCards = FindJobCards(driver);
        Console.WriteLine($"Cards encontrados na pagina inicial para {sourceLabel}: {initialJobCards.Count}");

        Console.WriteLine($"Extraindo vagas da pagina inicial para {sourceLabel}...");
        var initialExtraction = ExtractSimplifiedJobs(initialJobCards);

        foreach (var job in initialExtraction.RawLines)
        {
            Console.WriteLine(job);
        }

        allJobsLines.AddRange(initialExtraction.RawLines);
        allJobsData.AddRange(initialExtraction.StructuredJobs);

        CollectJobsFromPagination(driver, wait, allJobsLines, allJobsData, maxPagesPerCycle);

        Console.WriteLine($"Coleta concluida para {sourceLabel}. Total bruto acumulado: {allJobsData.Count}");
        return true;
    }

    private static void CollectJobsFromPagination(
        IWebDriver driver,
        WebDriverWait wait,
        List<string> allJobsLines,
        List<(string Titulo, string Empresa, string Localizacao, string Link)> allJobsData,
        int maxPagesPerCycle)
    {
        var hasMorePages = true;
        var pagesCollected = 1;
        var paginationWaitTimeout = wait.Timeout > TimeSpan.FromSeconds(10)
            ? wait.Timeout
            : TimeSpan.FromSeconds(25);
        Console.WriteLine("Iniciando paginação...");

        while (hasMorePages)
        {
            if (maxPagesPerCycle > 0 && pagesCollected >= maxPagesPerCycle)
            {
                Console.WriteLine($"Limite de páginas por ciclo atingido ({maxPagesPerCycle}).");
                break;
            }

            if (!TryResolveNextPaginationButton(driver, out var nextButton, out var nextButtonInfo) || nextButton == null)
            {
                hasMorePages = false;
                continue;
            }

            Console.WriteLine($"Indo para próxima página... ({nextButtonInfo})");
            var currentPageNumber = GetCurrentPaginationPageNumber(driver);
            var firstCardKeyBeforeClick = GetFirstJobCardKey(driver);

            if (!TryAdvancePagination(driver, nextButton, currentPageNumber, firstCardKeyBeforeClick, paginationWaitTimeout))
            {
                Console.WriteLine("Próximo botão de paginação não avançou a lista a tempo. Encerrando paginação desta execução.");
                break;
            }

            pagesCollected++;
            var currentPageAfterNavigation = GetCurrentPaginationPageNumber(driver);
            if (currentPageAfterNavigation > 0)
            {
                Console.WriteLine($"Página atual após paginação: {currentPageAfterNavigation}");
            }

            var pageCards = FindJobCards(driver);
            Console.WriteLine($"Cards encontrados na página: {pageCards.Count}");

            var pageExtraction = ExtractSimplifiedJobs(pageCards);
            allJobsLines.AddRange(pageExtraction.RawLines);
            allJobsData.AddRange(pageExtraction.StructuredJobs);
        }
    }

    private static bool TryResolveNextPaginationButton(IWebDriver driver, out IWebElement? nextButton, out string nextButtonInfo)
    {
        nextButton = null;
        nextButtonInfo = "sem informacao";

        try
        {
            var paginationButtons = driver.FindElements(By.CssSelector("ul.jobs-search-pagination__pages button.jobs-search-pagination__indicator-button"));
            var currentPageIndex = -1;

            for (var index = 0; index < paginationButtons.Count; index++)
            {
                if (string.Equals(paginationButtons[index].GetAttribute("aria-current"), "page", StringComparison.OrdinalIgnoreCase))
                {
                    currentPageIndex = index;
                    break;
                }
            }

            if (currentPageIndex >= 0)
            {
                for (var index = currentPageIndex + 1; index < paginationButtons.Count; index++)
                {
                    var candidate = paginationButtons[index];
                    if (IsPaginationButtonDisabled(candidate))
                    {
                        continue;
                    }

                    nextButton = candidate;
                    nextButtonInfo = $"indicador index={index}, texto='{candidate.Text?.Trim()}', aria-label='{candidate.GetAttribute("aria-label")}'";
                    return true;
                }
            }

            var nextCandidates = driver.FindElements(By.XPath(
                "//button[contains(translate(@aria-label, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'next') or " +
                "contains(translate(@aria-label, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'próxima') or " +
                "contains(translate(@aria-label, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'proxima')]"));

            foreach (var candidate in nextCandidates)
            {
                if (IsPaginationButtonDisabled(candidate))
                {
                    continue;
                }

                nextButton = candidate;
                nextButtonInfo = $"aria-next, texto='{candidate.Text?.Trim()}', aria-label='{candidate.GetAttribute("aria-label")}'";
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao localizar próximo botão de paginação: {ex.Message}");
        }

        return false;
    }

    private static bool TryAdvancePagination(
        IWebDriver driver,
        IWebElement? nextButton,
        int currentPageNumber,
        string firstCardKeyBeforeClick,
        TimeSpan timeout)
    {
        if (nextButton == null)
        {
            return false;
        }

        if (!TryClickPaginationButton(driver, nextButton))
        {
            return false;
        }

        if (WaitForPaginationAdvance(driver, currentPageNumber, firstCardKeyBeforeClick, timeout))
        {
            PauseAfterPaginationAdvance();
            return true;
        }

        Console.WriteLine("Primeira tentativa de paginação não avançou. Tentando novamente...");

        if (!TryResolveNextPaginationButton(driver, out var retryButton, out _ ) || retryButton == null)
        {
            return false;
        }

        if (!TryClickPaginationButton(driver, retryButton))
        {
            return false;
        }

        if (WaitForPaginationAdvance(driver, currentPageNumber, firstCardKeyBeforeClick, timeout))
        {
            PauseAfterPaginationAdvance();
            return true;
        }

        return false;
    }

    private static bool WaitForPaginationAdvance(
        IWebDriver driver,
        int previousPageNumber,
        string previousFirstCardKey,
        TimeSpan timeout)
    {
        try
        {
            var transitionWait = new WebDriverWait(driver, timeout);
            return transitionWait.Until(d =>
            {
                var currentPageNumber = GetCurrentPaginationPageNumber(d);
                if (previousPageNumber > 0 && currentPageNumber > previousPageNumber)
                {
                    return true;
                }

                var currentFirstCardKey = GetFirstJobCardKey(d);
                if (!string.IsNullOrWhiteSpace(previousFirstCardKey) &&
                    !string.IsNullOrWhiteSpace(currentFirstCardKey) &&
                    !string.Equals(previousFirstCardKey, currentFirstCardKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            });
        }
        catch (WebDriverTimeoutException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryClickPaginationButton(IWebDriver driver, IWebElement button)
    {
        TryScrollElementIntoViewHumanized(driver, button);

        try
        {
            ClickElementRobust(driver, button);
            return true;
        }
        catch
        {
            try
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", button);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static bool IsPaginationButtonDisabled(IWebElement button)
    {
        try
        {
            var disabled = button.GetAttribute("disabled");
            if (!string.IsNullOrWhiteSpace(disabled))
            {
                return true;
            }

            var ariaDisabled = button.GetAttribute("aria-disabled");
            return string.Equals(ariaDisabled, "true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static int GetCurrentPaginationPageNumber(IWebDriver driver)
    {
        try
        {
            var paginationButtons = driver.FindElements(By.CssSelector("ul.jobs-search-pagination__pages button.jobs-search-pagination__indicator-button"));
            for (var index = 0; index < paginationButtons.Count; index++)
            {
                var button = paginationButtons[index];
                if (!string.Equals(button.GetAttribute("aria-current"), "page", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var pageNumber = ExtractFirstPositiveInteger($"{button.Text} {button.GetAttribute("aria-label")}");
                if (pageNumber > 0)
                {
                    return pageNumber;
                }

                return index + 1;
            }
        }
        catch
        {
            // Ignora erro e retorna desconhecido.
        }

        return -1;
    }

    private static string GetFirstJobCardKey(IWebDriver driver)
    {
        try
        {
            var cards = FindJobCards(driver);
            foreach (var card in cards)
            {
                var link = GetAttributeBySelectors(card, "href",
                    "a.job-card-container__link",
                    "a.job-card-list__title");

                var normalizedLink = NormalizeLinkedInJobLink(link);
                if (!string.IsNullOrWhiteSpace(normalizedLink))
                {
                    return normalizedLink;
                }

                var text = card.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (text.Length > 120)
                    {
                        return text.Substring(0, 120);
                    }

                    return text;
                }
            }
        }
        catch
        {
            // Ignora erro e retorna vazio.
        }

        return string.Empty;
    }

    private static int ExtractFirstPositiveInteger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return -1;
        }

        var digits = string.Empty;
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsDigit(current))
            {
                digits += current;
                continue;
            }

            if (digits.Length > 0)
            {
                break;
            }
        }

        if (digits.Length == 0)
        {
            return -1;
        }

        return int.TryParse(digits, out var parsed) ? parsed : -1;
    }

    private static List<(string Titulo, string Empresa, string Localizacao, string Link)> NormalizeAndDeduplicateJobs(
        List<(string Titulo, string Empresa, string Localizacao, string Link)> jobs)
    {
        var normalizedJobs = new List<(string Titulo, string Empresa, string Localizacao, string Link)>();
        var seenLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var job in jobs)
        {
            var rawLink = (job.Link ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawLink))
            {
                continue;
            }

            if (IsBudgetExhaustedJobLink(rawLink))
            {
                continue;
            }

            var normalizedLink = NormalizeLinkedInJobLink(rawLink);
            if (string.IsNullOrWhiteSpace(normalizedLink))
            {
                continue;
            }

            if (IsIgnoredJobLinkInCurrentRun(normalizedLink))
            {
                continue;
            }

            if (!seenLinks.Add(normalizedLink))
            {
                continue;
            }

            normalizedJobs.Add((job.Titulo, job.Empresa, job.Localizacao, normalizedLink));
        }

        return normalizedJobs;
    }

    private static string BuildJobLine((string Titulo, string Empresa, string Localizacao, string Link) job)
    {
        return $"{job.Titulo} | {job.Empresa} | {job.Localizacao} | {job.Link}";
    }

    private static IReadOnlyCollection<IWebElement> FindJobCards(IWebDriver driver)
    {
        var cards = driver.FindElements(By.CssSelector("ul.jobs-search-results__list > li, ul.scaffold-layout__list-container > li, li.jobs-search-results__list-item, ul.jobs-search__results-list > li, ul.scaffold-layout__list-container li, div.job-card-container, a.job-card-container__link"));
        return cards;
    }

    private static bool WaitForJobsResults(IWebDriver driver)
    {
        try
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));
            return wait.Until(d => HasAnyJobCard(d));
        }
        catch (WebDriverTimeoutException)
        {
            try
            {
                driver.Navigate().Refresh();
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));
                return wait.Until(d => HasAnyJobCard(d));
            }
            catch
            {
                return false;
            }
        }
    }

    private static bool HasAnyJobCard(IWebDriver driver)
    {
        try
        {
            var cards = driver.FindElements(By.CssSelector("li.jobs-search-results__list-item, div.job-card-container, a.job-card-container__link, ul.jobs-search__results-list li"));
            if (cards.Count > 0)
            {
                return true;
            }

            try
            {
                var js = (IJavaScriptExecutor)driver;
                js.ExecuteScript("window.scrollBy(0, 600);");
            }
            catch
            {
                // Ignora falha de script e continua tentativa.
            }

            cards = driver.FindElements(By.CssSelector("li.jobs-search-results__list-item, div.job-card-container, a.job-card-container__link, ul.jobs-search__results-list li"));
            return cards.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetTextBySelectors(IWebElement root, params string[] selectors)
    {
        foreach (var selector in selectors)
        {
            try
            {
                var element = root.FindElement(By.CssSelector(selector));
                var text = element?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch (NoSuchElementException)
            {
                continue;
            }
        }

        return string.Empty;
    }

    private static string GetAttributeBySelectors(IWebElement root, string attributeName, params string[] selectors)
    {
        foreach (var selector in selectors)
        {
            try
            {
                var element = root.FindElement(By.CssSelector(selector));
                var value = element?.GetAttribute(attributeName)?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch (NoSuchElementException)
            {
                continue;
            }
        }

        return string.Empty;
    }

    private static bool HasSimplifiedApplication(IWebElement card)
    {
        try
        {
            var badgeElements = card.FindElements(By.XPath(
                ".//*[contains(translate(normalize-space(.), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'candidatura simplificada') or " +
                "contains(translate(normalize-space(.), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'easy apply') or " +
                "contains(translate(@aria-label, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'candidatura simplificada') or " +
                "contains(translate(@aria-label, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'easy apply')]"));
            if (badgeElements.Count > 0)
            {
                return true;
            }

            var text = card.Text;
            return text != null &&
                   (text.Contains("Candidatura simplificada", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Easy Apply", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static void PrintVagasTabela(List<(string Titulo, string Empresa, string Localizacao, string Link)> vagas)
    {
        const int titleWidth = 40;
        const int companyWidth = 30;
        const int locationWidth = 25;

        string Truncate(string value, int width)
        {
            if (string.IsNullOrWhiteSpace(value)) return "".PadRight(width);
            var v = value.Trim();
            if (v.Length <= width) return v.PadRight(width);
            return v.Substring(0, Math.Max(0, width - 1)) + "…";
        }

        var header = $"| {"Título".PadRight(titleWidth)} | {"Empresa".PadRight(companyWidth)} | {"Localização".PadRight(locationWidth)} | Link";
        var separator = $"|-{new string('-', titleWidth)}-|-{new string('-', companyWidth)}-|-{new string('-', locationWidth)}-|------";

        Console.WriteLine(header);
        Console.WriteLine(separator);

        foreach (var vaga in vagas)
        {
            Console.WriteLine($"| {Truncate(vaga.Titulo, titleWidth)} | {Truncate(vaga.Empresa, companyWidth)} | {Truncate(vaga.Localizacao, locationWidth)} | {vaga.Link}");
        }
    }
}
