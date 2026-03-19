using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Linq;

partial class Program
{
    private static bool TryPrepareJobsSearchEntry(IWebDriver driver, string searchTerm)
    {
        var selectedSearchTerm = string.IsNullOrWhiteSpace(searchTerm) ? "PHP" : searchTerm.Trim();

        try
        {
            if (!TryOpenJobsAreaByNavigation(driver))
            {
                Console.WriteLine("Não foi possível clicar no menu de Vagas. Usando navegação direta para /jobs/.");
                driver.Navigate().GoToUrl("https://www.linkedin.com/jobs/");
                WaitForJobPageReady(driver);
            }

            if (!TrySubmitJobsSearchKeyword(driver, selectedSearchTerm))
            {
                Console.WriteLine("Campo de busca não encontrado/interagível. Usando fallback de URL com termo e filtro Easy Apply.");
                return TryOpenJobsSearchViaUrl(driver, selectedSearchTerm);
            }

            if (TryEnableEasyApplyFilterFromResults(driver))
            {
                return true;
            }

            Console.WriteLine("Filtro de Candidatura simplificada não foi confirmado por UI. Aplicando fallback por URL.");
            return TryOpenJobsSearchViaUrl(driver, selectedSearchTerm);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Falha ao preparar entrada via busca de vagas: {ex.Message}");
            return TryOpenJobsSearchViaUrl(driver, selectedSearchTerm);
        }
    }

    private static IReadOnlyList<string> GetJobSearchTermsForCurrentCycle(IReadOnlyList<string> searchTerms)
    {
        var normalizedTerms = (searchTerms ?? Array.Empty<string>())
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedTerms.Count == 0)
        {
            return new[] { "PHP" };
        }

        lock (JobSearchTermSelectionLock)
        {
            if (JobSearchTermSelectionIndex < 0)
            {
                JobSearchTermSelectionIndex = 0;
            }

            if (JobSearchTermSelectionIndex >= normalizedTerms.Count)
            {
                JobSearchTermSelectionIndex = 0;
            }

            var selectedIndex = JobSearchTermSelectionIndex;
            JobSearchTermSelectionIndex = (JobSearchTermSelectionIndex + 1) % normalizedTerms.Count;

            var orderedTerms = new List<string>(normalizedTerms.Count);
            for (var offset = 0; offset < normalizedTerms.Count; offset++)
            {
                orderedTerms.Add(normalizedTerms[(selectedIndex + offset) % normalizedTerms.Count]);
            }

            return orderedTerms;
        }
    }

    private static bool TryOpenJobsAreaByNavigation(IWebDriver driver)
    {
        try
        {
            foreach (var candidate in GetRankedJobsNavigationCandidates(driver))
            {
                try
                {
                    ClickElementRobust(driver, candidate);
                    if (WaitForJobsAreaNavigation(driver))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Tenta próximo candidato.
                }
            }
        }
        catch
        {
            // Cai no fallback da navegação direta.
        }

        return IsJobsPageUrl(driver.Url);
    }

    private static IEnumerable<IWebElement> GetRankedJobsNavigationCandidates(IWebDriver driver)
    {
        var ranked = new List<(IWebElement Element, int Score)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var xpathCandidates = FindElementsSafe(driver, By.XPath(
            "//header//*[self::a or self::button][contains(@href,'/jobs/') or " +
            "contains(translate(@aria-label,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'vagas') or " +
            "contains(translate(@aria-label,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'jobs') or " +
            "contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'vagas') or " +
            "contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'jobs')] | " +
            "//nav//*[self::a or self::button][contains(@href,'/jobs/') or " +
            "contains(translate(@aria-label,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'vagas') or " +
            "contains(translate(@aria-label,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'jobs') or " +
            "contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'vagas') or " +
            "contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'jobs')] | " +
            "//*[self::a or self::button][contains(@href,'/jobs/') or " +
            "contains(translate(@aria-label,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'vagas') or " +
            "contains(translate(@aria-label,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'jobs')]"));

        AddRankedElements(ranked, seen, xpathCandidates, ScoreJobsNavigationCandidate);
        return ranked.OrderByDescending(item => item.Score).Select(item => item.Element);
    }

    private static bool WaitForJobsAreaNavigation(IWebDriver driver)
    {
        try
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
            var navigated = wait.Until(d => IsJobsPageUrl(d.Url));
            if (!navigated)
            {
                return false;
            }

            WaitForJobPageReady(driver);
            return true;
        }
        catch
        {
            return IsJobsPageUrl(driver.Url);
        }
    }

    private static int ScoreJobsNavigationCandidate(IWebElement element)
    {
        if (!IsElementInteractable(element))
        {
            return int.MinValue;
        }

        var text = SafeGetElementText(element);
        var ariaLabel = SafeGetAttribute(element, "aria-label");
        var href = SafeGetAttribute(element, "href");
        var dataControlName = SafeGetAttribute(element, "data-control-name");
        var descriptor = BuildDescriptor(text, ariaLabel, href, dataControlName);
        var score = 0;

        if (href.Contains("/jobs/", StringComparison.OrdinalIgnoreCase))
        {
            score += 260;
        }

        if (dataControlName.Contains("jobs", StringComparison.OrdinalIgnoreCase))
        {
            score += 180;
        }

        if (ariaLabel.Contains("vagas", StringComparison.OrdinalIgnoreCase) ||
            ariaLabel.Contains("jobs", StringComparison.OrdinalIgnoreCase))
        {
            score += 180;
        }

        if (text.Equals("Vagas", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Jobs", StringComparison.OrdinalIgnoreCase))
        {
            score += 220;
        }
        else if (text.Contains("vagas", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("jobs", StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }

        if (SafeGetTagName(element).Equals("a", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        if (descriptor.Contains("notifica", StringComparison.OrdinalIgnoreCase) ||
            descriptor.Contains("notification", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        return score;
    }

    private static bool TrySubmitJobsSearchKeyword(IWebDriver driver, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        var searchInput = FindBestJobsSearchInput(driver);

        if (searchInput == null)
        {
            return false;
        }

        var previousUrl = driver.Url;

        try
        {
            if (!TryPopulateAndSubmitJobsSearchInput(driver, searchInput, term))
            {
                return false;
            }

            return WaitForJobsSearchTransition(driver, previousUrl, term);
        }
        catch
        {
            return false;
        }
    }

    private static IWebElement? FindBestJobsSearchInput(IWebDriver driver)
    {
        var ranked = new List<(IWebElement Element, int Score)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var searchContainers = FindElementsSafe(driver, By.CssSelector(
            "div[role='search'], form[role='search'], div[class*='jobs-search-box'], section[class*='jobs-search-box'], div[data-testid='job-search-bar']"));

        foreach (var container in searchContainers)
        {
            AddSearchInputCandidates(container, ranked, seen, preferredContext: true);
        }

        AddSearchInputCandidates(driver, ranked, seen, preferredContext: false);

        return ranked
            .OrderByDescending(item => item.Score)
            .Select(item => item.Element)
            .FirstOrDefault();
    }

    private static void AddSearchInputCandidates(ISearchContext context, List<(IWebElement Element, int Score)> ranked, HashSet<string> seen, bool preferredContext)
    {
        var inputs = FindElementsSafe(context, By.CssSelector("input, textarea"));
        AddRankedElements(ranked, seen, inputs, element => ScoreJobsSearchInput(element, preferredContext));
    }

    private static int ScoreJobsSearchInput(IWebElement element, bool preferredContext)
    {
        if (!IsElementInteractable(element))
        {
            return int.MinValue;
        }

        var descriptor = BuildDescriptor(
            SafeGetAttribute(element, "componentkey"),
            SafeGetAttribute(element, "data-testid"),
            SafeGetAttribute(element, "placeholder"),
            SafeGetAttribute(element, "aria-label"),
            SafeGetAttribute(element, "role"),
            SafeGetAttribute(element, "aria-autocomplete"),
            SafeGetAttribute(element, "id"),
            SafeGetAttribute(element, "name"),
            SafeGetAttribute(element, "class"),
            SafeGetAttribute(element, "type"));

        if (string.IsNullOrWhiteSpace(descriptor))
        {
            return int.MinValue;
        }

        var score = preferredContext ? 120 : 0;

        if (descriptor.Contains("jobsearchbox", StringComparison.OrdinalIgnoreCase))
        {
            score += 420;
        }

        if (descriptor.Contains("typeahead-input", StringComparison.OrdinalIgnoreCase))
        {
            score += 220;
        }

        if (descriptor.Contains("cargo", StringComparison.OrdinalIgnoreCase) ||
            descriptor.Contains("compet", StringComparison.OrdinalIgnoreCase) ||
            descriptor.Contains("empresa", StringComparison.OrdinalIgnoreCase) ||
            descriptor.Contains("company", StringComparison.OrdinalIgnoreCase) ||
            descriptor.Contains("skill", StringComparison.OrdinalIgnoreCase) ||
            descriptor.Contains("keyword", StringComparison.OrdinalIgnoreCase))
        {
            score += 180;
        }

        if (descriptor.Contains("combobox", StringComparison.OrdinalIgnoreCase) ||
            descriptor.Contains("list", StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (descriptor.Contains("search", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        if (descriptor.Contains("location", StringComparison.OrdinalIgnoreCase) ||
            descriptor.Contains("localiza", StringComparison.OrdinalIgnoreCase) ||
            descriptor.Contains("where", StringComparison.OrdinalIgnoreCase) ||
            descriptor.Contains("onde", StringComparison.OrdinalIgnoreCase))
        {
            score -= 320;
        }

        return score > 0 ? score : int.MinValue;
    }

    private static bool TryPopulateAndSubmitJobsSearchInput(IWebDriver driver, IWebElement searchInput, string term)
    {
        try
        {
            TryScrollElementIntoViewHumanized(driver, searchInput);
            searchInput.Click();
        }
        catch
        {
            try
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus();", searchInput);
            }
            catch
            {
                return false;
            }
        }

        try
        {
            searchInput.SendKeys(Keys.Control + "a");
            searchInput.SendKeys(Keys.Delete);
            searchInput.Clear();
        }
        catch
        {
            try
            {
                SetInputValueWithJavaScript(driver, searchInput, string.Empty);
            }
            catch
            {
                // Continua para tentar preencher mesmo assim.
            }
        }

        PauseBeforeClick();

        try
        {
            searchInput.SendKeys(term);
            PauseBeforeClick();
            searchInput.SendKeys(Keys.Enter);
            return true;
        }
        catch
        {
            try
            {
                SetInputValueWithJavaScript(driver, searchInput, term);
                PauseBeforeClick();
                searchInput.SendKeys(Keys.End);
                searchInput.SendKeys(Keys.Enter);
                return true;
            }
            catch
            {
                try
                {
                    SetInputValueWithJavaScript(driver, searchInput, term);
                    ((IJavaScriptExecutor)driver).ExecuteScript(
                        "const input = arguments[0];" +
                        "const keyboardOptions = { bubbles: true, cancelable: true, key: 'Enter', code: 'Enter' };" +
                        "input.dispatchEvent(new KeyboardEvent('keydown', keyboardOptions));" +
                        "input.dispatchEvent(new KeyboardEvent('keypress', keyboardOptions));" +
                        "input.dispatchEvent(new KeyboardEvent('keyup', keyboardOptions));" +
                        "const form = input.closest('form');" +
                        "if (form) { if (typeof form.requestSubmit === 'function') { form.requestSubmit(); } else { form.submit(); } }",
                        searchInput);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    private static void SetInputValueWithJavaScript(IWebDriver driver, IWebElement input, string value)
    {
        ((IJavaScriptExecutor)driver).ExecuteScript(
            "const input = arguments[0];" +
            "const value = arguments[1];" +
            "const descriptor = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value');" +
            "const setter = descriptor && descriptor.set;" +
            "if (setter) { setter.call(input, value); } else { input.value = value; }" +
            "input.dispatchEvent(new Event('input', { bubbles: true }));" +
            "input.dispatchEvent(new Event('change', { bubbles: true }));",
            input,
            value);
    }

    private static bool WaitForJobsSearchTransition(IWebDriver driver, string previousUrl, string term)
    {
        try
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
            var transitioned = wait.Until(d =>
            {
                var currentUrl = d.Url ?? string.Empty;
                if (!IsJobsPageUrl(currentUrl))
                {
                    return false;
                }

                var keywords = (TryGetQueryParamValue(currentUrl, "keywords") ?? string.Empty).Replace("+", " ");
                if (!string.IsNullOrWhiteSpace(keywords) &&
                    keywords.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return !string.Equals(previousUrl, currentUrl, StringComparison.OrdinalIgnoreCase);
            });

            if (!transitioned)
            {
                return false;
            }

            return WaitForJobsResults(driver);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryEnableEasyApplyFilterFromResults(IWebDriver driver)
    {
        if (IsEasyApplyFilterActive(driver))
        {
            return true;
        }

        WaitForPotentialJobsFilters(driver);

        if (TryClickEasyApplyFilterCandidate(driver, driver) && WaitForEasyApplyFilterActivation(driver))
        {
            return true;
        }

        if (TryOpenAllFiltersPanel(driver, out var filtersDialog) && filtersDialog != null)
        {
            Console.WriteLine("Tentando localizar Candidatura simplificada pelo modal de filtros.");
            var toggled = TryClickEasyApplyFilterCandidate(driver, filtersDialog);
            if (toggled)
            {
                TrySubmitFiltersDialog(driver, filtersDialog);
                if (WaitForEasyApplyFilterActivation(driver))
                {
                    return true;
                }
            }
        }

        return IsEasyApplyFilterActive(driver);
    }

    private static void WaitForPotentialJobsFilters(IWebDriver driver)
    {
        try
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
            wait.Until(d =>
            {
                try
                {
                    return d.FindElements(By.CssSelector("button, label, input[type='checkbox'], div[role='button'], span[role='button']")).Count > 0;
                }
                catch
                {
                    return false;
                }
            });
        }
        catch
        {
            // Continua tentativa mesmo sem confirmação explícita.
        }
    }

    private static bool TryClickEasyApplyFilterCandidate(IWebDriver driver, ISearchContext context)
    {
        foreach (var candidate in GetRankedEasyApplyFilterCandidates(context))
        {
            try
            {
                if (SafeGetTagName(candidate).Equals("input", StringComparison.OrdinalIgnoreCase) &&
                    SafeGetAttribute(candidate, "type").Equals("checkbox", StringComparison.OrdinalIgnoreCase) &&
                    candidate.Selected)
                {
                    return true;
                }

                ClickElementRobust(driver, candidate);
                PauseBetweenFlowSteps();
                return true;
            }
            catch
            {
                // Tenta próximo candidato.
            }
        }

        return false;
    }

    private static IEnumerable<IWebElement> GetRankedEasyApplyFilterCandidates(ISearchContext context)
    {
        var ranked = new List<(IWebElement Element, int Score)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filterElements = FindElementsSafe(context, By.CssSelector("button, label, input, div[role='button'], span[role='button']"));
        AddRankedElements(ranked, seen, filterElements, ScoreEasyApplyFilterCandidate);
        return ranked.OrderByDescending(item => item.Score).Select(item => item.Element);
    }

    private static int ScoreEasyApplyFilterCandidate(IWebElement element)
    {
        var descriptor = BuildDescriptor(
            SafeGetElementText(element),
            SafeGetAttribute(element, "aria-label"),
            SafeGetAttribute(element, "id"),
            SafeGetAttribute(element, "name"),
            SafeGetAttribute(element, "for"),
            SafeGetAttribute(element, "class"));

        var referencesFilter = descriptor.Contains("candidatura simplificada", StringComparison.OrdinalIgnoreCase) ||
                               descriptor.Contains("easy apply", StringComparison.OrdinalIgnoreCase) ||
                               descriptor.Contains("f_al", StringComparison.OrdinalIgnoreCase);

        if (!referencesFilter)
        {
            return int.MinValue;
        }

        if (!IsElementInteractable(element) &&
            !(SafeGetTagName(element).Equals("input", StringComparison.OrdinalIgnoreCase) && descriptor.Contains("f_al", StringComparison.OrdinalIgnoreCase)))
        {
            return int.MinValue;
        }

        var score = 0;

        if (descriptor.Contains("f_al", StringComparison.OrdinalIgnoreCase))
        {
            score += 320;
        }

        if (descriptor.Contains("candidatura simplificada", StringComparison.OrdinalIgnoreCase))
        {
            score += 260;
        }

        if (descriptor.Contains("easy apply", StringComparison.OrdinalIgnoreCase))
        {
            score += 240;
        }

        if (SafeGetTagName(element).Equals("input", StringComparison.OrdinalIgnoreCase) &&
            SafeGetAttribute(element, "type").Equals("checkbox", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        return score;
    }

    private static bool TryOpenAllFiltersPanel(IWebDriver driver, out IWebElement? filtersDialog)
    {
        filtersDialog = TryFindFiltersDialog(driver);
        if (filtersDialog != null)
        {
            return true;
        }

        foreach (var candidate in GetRankedAllFiltersButtons(driver))
        {
            try
            {
                ClickElementRobust(driver, candidate);
                PauseBetweenFlowSteps();
                filtersDialog = WaitForFiltersDialog(driver);
                if (filtersDialog != null)
                {
                    return true;
                }
            }
            catch
            {
                // Tenta próximo candidato.
            }
        }

        return false;
    }

    private static IEnumerable<IWebElement> GetRankedAllFiltersButtons(IWebDriver driver)
    {
        var ranked = new List<(IWebElement Element, int Score)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filterButtons = FindElementsSafe(driver, By.CssSelector("button, div[role='button'], span[role='button']"));
        AddRankedElements(ranked, seen, filterButtons, ScoreAllFiltersButton);
        return ranked.OrderByDescending(item => item.Score).Select(item => item.Element);
    }

    private static int ScoreAllFiltersButton(IWebElement element)
    {
        if (!IsElementInteractable(element))
        {
            return int.MinValue;
        }

        var descriptor = BuildDescriptor(
            SafeGetElementText(element),
            SafeGetAttribute(element, "aria-label"),
            SafeGetAttribute(element, "class"));

        var score = 0;

        if (descriptor.Contains("todos os filtros", StringComparison.OrdinalIgnoreCase) ||
            descriptor.Contains("all filters", StringComparison.OrdinalIgnoreCase))
        {
            score += 320;
        }
        else if (descriptor.Contains("filtros", StringComparison.OrdinalIgnoreCase) ||
                 descriptor.Contains("filters", StringComparison.OrdinalIgnoreCase))
        {
            score += 160;
        }

        return score > 0 ? score : int.MinValue;
    }

    private static IWebElement? WaitForFiltersDialog(IWebDriver driver)
    {
        try
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            return wait.Until(TryFindFiltersDialog);
        }
        catch
        {
            return TryFindFiltersDialog(driver);
        }
    }

    private static IWebElement? TryFindFiltersDialog(IWebDriver driver)
    {
        return FindElementsSafe(driver, By.CssSelector("div[role='dialog'], section[role='dialog'], div.artdeco-modal, div.artdeco-modal__content"))
            .FirstOrDefault(IsElementInteractable);
    }

    private static bool TrySubmitFiltersDialog(IWebDriver driver, ISearchContext filtersDialog)
    {
        foreach (var candidate in GetRankedFiltersApplyButtons(filtersDialog))
        {
            try
            {
                ClickElementRobust(driver, candidate);
                PauseBetweenFlowSteps();
                return true;
            }
            catch
            {
                // Tenta próximo candidato.
            }
        }

        return false;
    }

    private static IEnumerable<IWebElement> GetRankedFiltersApplyButtons(ISearchContext filtersDialog)
    {
        var ranked = new List<(IWebElement Element, int Score)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buttons = FindElementsSafe(filtersDialog, By.CssSelector("button, div[role='button'], span[role='button']"));
        AddRankedElements(ranked, seen, buttons, ScoreFiltersApplyButton);
        return ranked.OrderByDescending(item => item.Score).Select(item => item.Element);
    }

    private static int ScoreFiltersApplyButton(IWebElement element)
    {
        if (!IsElementInteractable(element))
        {
            return int.MinValue;
        }

        var descriptor = BuildDescriptor(SafeGetElementText(element), SafeGetAttribute(element, "aria-label"));
        var score = 0;

        if (descriptor.Contains("mostrar resultados", StringComparison.OrdinalIgnoreCase) ||
            descriptor.Contains("show results", StringComparison.OrdinalIgnoreCase))
        {
            score += 320;
        }
        else if (descriptor.Contains("aplicar", StringComparison.OrdinalIgnoreCase) ||
                 descriptor.Contains("apply", StringComparison.OrdinalIgnoreCase))
        {
            score += 180;
        }

        return score > 0 ? score : int.MinValue;
    }

    private static bool WaitForEasyApplyFilterActivation(IWebDriver driver)
    {
        try
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
            return wait.Until(IsEasyApplyFilterActive);
        }
        catch
        {
            return IsEasyApplyFilterActive(driver);
        }
    }

    private static bool IsEasyApplyFilterActive(IWebDriver driver)
    {
        var fAl = TryGetQueryParamValue(driver.Url, "f_AL") ?? string.Empty;
        if (fAl.Equals("true", StringComparison.OrdinalIgnoreCase) || fAl.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var activeCandidates = driver.FindElements(By.XPath(
                "//button[(@aria-pressed='true' or @aria-checked='true' or contains(@class,'selected') or contains(@class,'active')) and " +
                "(contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'candidatura simplificada') or " +
                "contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'easy apply') or " +
                "contains(translate(@aria-label,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'candidatura simplificada') or " +
                "contains(translate(@aria-label,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'easy apply'))] | " +
                "//label[(contains(@class,'selected') or contains(@class,'active')) and " +
                "(contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'candidatura simplificada') or " +
                "contains(translate(normalize-space(.),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'easy apply') or " +
                "contains(translate(@aria-label,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'candidatura simplificada') or " +
                "contains(translate(@aria-label,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'easy apply'))] | " +
                "//input[(contains(@id,'f_AL') or contains(@name,'f_AL')) and (@checked or @aria-checked='true')]"));

            return activeCandidates.Any(element =>
            {
                try { return element.Displayed; } catch { return false; }
            });
        }
        catch
        {
            return false;
        }
    }

    private static bool TryOpenJobsSearchViaUrl(IWebDriver driver, string searchTerm)
    {
        try
        {
            var keyword = string.IsNullOrWhiteSpace(searchTerm) ? "PHP" : searchTerm.Trim();
            var encodedKeyword = Uri.EscapeDataString(keyword);
            var fallbackUrl = $"{LinkedInJobsSearchUrl}?keywords={encodedKeyword}&f_AL=true&sortBy=DD&refresh=true";
            Console.WriteLine($"Abrindo fallback de busca por URL: {fallbackUrl}");
            driver.Navigate().GoToUrl(fallbackUrl);
            WaitForJobPageReady(driver);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Falha no fallback de URL da busca de vagas: {ex.Message}");
            return false;
        }
    }

    private static bool IsJobsPageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return url.Contains("linkedin.com/jobs", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/jobs/", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/jobs/search", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<IWebElement> FindElementsSafe(ISearchContext context, By selector)
    {
        try
        {
            return context.FindElements(selector);
        }
        catch
        {
            return Array.Empty<IWebElement>();
        }
    }

    private static void AddRankedElements(List<(IWebElement Element, int Score)> ranked, HashSet<string> seen, IEnumerable<IWebElement> candidates, Func<IWebElement, int> scorer)
    {
        foreach (var candidate in candidates)
        {
            try
            {
                var score = scorer(candidate);
                if (score == int.MinValue)
                {
                    continue;
                }

                var key = BuildElementIdentityKey(candidate);
                if (!seen.Add(key))
                {
                    continue;
                }

                ranked.Add((candidate, score));
            }
            catch
            {
                // Tenta próximo candidato.
            }
        }
    }

    private static bool IsElementInteractable(IWebElement element)
    {
        try
        {
            return element.Displayed && element.Enabled;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeGetTagName(IWebElement element)
    {
        try
        {
            return element.TagName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeGetElementText(IWebElement element)
    {
        try
        {
            return (element.Text ?? string.Empty).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeGetAttribute(IWebElement element, string attributeName)
    {
        try
        {
            return (element.GetAttribute(attributeName) ?? string.Empty).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildDescriptor(params string[] parts)
    {
        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildElementIdentityKey(IWebElement element)
    {
        return BuildDescriptor(
            SafeGetTagName(element),
            SafeGetAttribute(element, "id"),
            SafeGetAttribute(element, "name"),
            SafeGetAttribute(element, "for"),
            SafeGetAttribute(element, "href"),
            SafeGetAttribute(element, "aria-label"),
            SafeGetElementText(element));
    }
}