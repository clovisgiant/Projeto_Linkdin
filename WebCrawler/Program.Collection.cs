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

    private static void CollectJobsFromPagination(
        IWebDriver driver,
        WebDriverWait wait,
        List<string> allJobsLines,
        List<(string Titulo, string Empresa, string Localizacao, string Link)> allJobsData,
        int maxPagesPerCycle)
    {
        var hasMorePages = true;
        var pagesCollected = 1;
        Console.WriteLine("Iniciando paginação...");

        while (hasMorePages)
        {
            if (maxPagesPerCycle > 0 && pagesCollected >= maxPagesPerCycle)
            {
                Console.WriteLine($"Limite de páginas por ciclo atingido ({maxPagesPerCycle}).");
                break;
            }

            var paginationButtons = driver.FindElements(By.CssSelector("ul.jobs-search-pagination__pages button.jobs-search-pagination__indicator-button"));
            var currentPageIndex = -1;

            for (var index = 0; index < paginationButtons.Count; index++)
            {
                if (paginationButtons[index].GetAttribute("aria-current") == "page")
                {
                    currentPageIndex = index;
                    break;
                }
            }

            if (currentPageIndex < 0 || currentPageIndex + 1 >= paginationButtons.Count)
            {
                hasMorePages = false;
                continue;
            }

            var nextButton = paginationButtons[currentPageIndex + 1];

            bool nextButtonReady;
            try
            {
                nextButtonReady = wait.Until(_ => nextButton.Displayed && nextButton.Enabled);
            }
            catch (WebDriverTimeoutException)
            {
                Console.WriteLine("Próximo botão de paginação não ficou clicável a tempo. Encerrando paginação desta execução.");
                break;
            }

            if (!nextButtonReady)
            {
                Console.WriteLine("Próximo botão de paginação não está pronto. Encerrando paginação desta execução.");
                break;
            }

            Console.WriteLine("Indo para próxima página...");
            nextButton.Click();
            Thread.Sleep(2500);
            pagesCollected++;

            var pageCards = FindJobCards(driver);
            Console.WriteLine($"Cards encontrados na página: {pageCards.Count}");

            var pageExtraction = ExtractSimplifiedJobs(pageCards);
            allJobsLines.AddRange(pageExtraction.RawLines);
            allJobsData.AddRange(pageExtraction.StructuredJobs);
        }
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
            var badgeElements = card.FindElements(By.XPath(".//*[contains(., 'Candidatura simplificada') or contains(@aria-label, 'Candidatura simplificada')]"));
            if (badgeElements.Count > 0)
            {
                return true;
            }

            var text = card.Text;
            return text != null && text.Contains("Candidatura simplificada", StringComparison.OrdinalIgnoreCase);
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
