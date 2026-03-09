using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

partial class Program
{
    private static string GetSuccessfulJobsCycleFilePath()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), SuccessfulJobsCycleFileName);
    }

    private static string GetSuccessfulJobsHistoryFilePath()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), SuccessfulJobsHistoryFileName);
    }

    private static void LoadSuccessfulJobsFromDisk()
    {
        if (!PersistSuccessfulLinksToFile)
        {
            Console.WriteLine("Persistencia de links de sucesso desativada por configuracao.");
            return;
        }

        try
        {
            var historyPath = GetSuccessfulJobsHistoryFilePath();
            if (!File.Exists(historyPath))
            {
                Console.WriteLine($"Arquivo historico de sucesso ainda nao existe: {historyPath}");
                return;
            }

            var loadedCount = 0;
            foreach (var line in File.ReadLines(historyPath))
            {
                var normalizedLink = NormalizeLinkedInJobLink(line);
                if (string.IsNullOrWhiteSpace(normalizedLink))
                {
                    continue;
                }

                lock (SuccessfulJobLinksLock)
                {
                    if (SuccessfulJobLinksPersisted.Add(normalizedLink))
                    {
                        loadedCount++;
                    }
                }
            }

            Console.WriteLine($"Links de candidaturas com sucesso carregados do historico: {loadedCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Falha ao carregar historico de candidaturas com sucesso: {ex.Message}");
        }
    }

    private static void StartSuccessfulJobsCycle()
    {
        lock (SuccessfulJobLinksLock)
        {
            SuccessfulJobLinksCurrentCycle.Clear();
        }

        try
        {
            File.WriteAllText(GetSuccessfulJobsCycleFilePath(), string.Empty);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Falha ao preparar arquivo de sucesso do ciclo: {ex.Message}");
        }
    }

    private static void RegisterSuccessfulApplication(string? link, string sourceContext)
    {
        var normalizedLink = NormalizeLinkedInJobLink(link);
        if (string.IsNullOrWhiteSpace(normalizedLink))
        {
            return;
        }

        bool addedInCurrentCycle;
        bool addedInHistory = false;

        lock (SuccessfulJobLinksLock)
        {
            addedInCurrentCycle = SuccessfulJobLinksCurrentCycle.Add(normalizedLink);
            if (PersistSuccessfulLinksToFile)
            {
                addedInHistory = SuccessfulJobLinksPersisted.Add(normalizedLink);
            }
        }

        if (!addedInCurrentCycle)
        {
            return;
        }

        try
        {
            File.AppendAllText(GetSuccessfulJobsCycleFilePath(), normalizedLink + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Falha ao registrar sucesso no arquivo do ciclo: {ex.Message}");
        }

        if (addedInHistory)
        {
            try
            {
                File.AppendAllText(GetSuccessfulJobsHistoryFilePath(), normalizedLink + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Falha ao registrar sucesso no historico: {ex.Message}");
            }
        }

        Console.WriteLine($"Vaga registrada como enviada com sucesso: {normalizedLink} (origem: {sourceContext})");
    }

    private static void PrintSuccessfulJobsCycleSummary()
    {
        List<string> successLinks;
        lock (SuccessfulJobLinksLock)
        {
            successLinks = SuccessfulJobLinksCurrentCycle.ToList();
        }

        Console.WriteLine($"\nCandidaturas enviadas com sucesso no ciclo: {successLinks.Count}");
        foreach (var link in successLinks)
        {
            Console.WriteLine($"[SUCESSO] {link}");
        }

        Console.WriteLine($"Arquivo de sucesso do ciclo: {GetSuccessfulJobsCycleFilePath()}");
        if (PersistSuccessfulLinksToFile)
        {
            Console.WriteLine($"Arquivo historico de sucesso: {GetSuccessfulJobsHistoryFilePath()}");
        }
    }

    private static string GetIgnoredJobLinksFilePath()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), IgnoredJobsFileName);
    }

    private static void LoadIgnoredJobLinksFromDisk()
    {
        if (!PersistIgnoredLinksToFile)
        {
            Console.WriteLine("Persistencia de links ignorados desativada por configuracao.");
            return;
        }

        try
        {
            var filePath = GetIgnoredJobLinksFilePath();
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Arquivo de links ignorados ainda nao existe: {filePath}");
                return;
            }

            var loadedCount = 0;
            foreach (var line in File.ReadLines(filePath))
            {
                var normalizedLink = NormalizeLinkedInJobLink(line);
                if (string.IsNullOrWhiteSpace(normalizedLink))
                {
                    continue;
                }

                lock (IgnoredJobLinksInRunLock)
                {
                    if (IgnoredJobLinksInRun.Add(normalizedLink))
                    {
                        loadedCount++;
                    }
                }
            }

            Console.WriteLine($"Links ignorados carregados de arquivo: {loadedCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Falha ao carregar links ignorados do arquivo: {ex.Message}");
        }
    }

    private static void PersistIgnoredJobLinkToDisk(string normalizedLink)
    {
        if (!PersistIgnoredLinksToFile || string.IsNullOrWhiteSpace(normalizedLink))
        {
            return;
        }

        try
        {
            var filePath = GetIgnoredJobLinksFilePath();
            File.AppendAllText(filePath, normalizedLink + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Falha ao persistir link ignorado em arquivo: {ex.Message}");
        }
    }

    private static void IgnoreJobLinkInCurrentRun(string? link, string reason)
    {
        var normalizedLink = NormalizeLinkedInJobLink(link);
        if (string.IsNullOrWhiteSpace(normalizedLink))
        {
            return;
        }

        var added = false;

        lock (IgnoredJobLinksInRunLock)
        {
            added = IgnoredJobLinksInRun.Add(normalizedLink);
        }

        if (!added)
        {
            return;
        }

        Console.WriteLine($"Link adicionado à lista de ignorados desta execução: {normalizedLink} (motivo: {reason})");
        PersistIgnoredJobLinkToDisk(normalizedLink);
    }

    private static bool IsIgnoredJobLinkInCurrentRun(string? link)
    {
        var normalizedLink = NormalizeLinkedInJobLink(link);
        if (string.IsNullOrWhiteSpace(normalizedLink))
        {
            return false;
        }

        lock (IgnoredJobLinksInRunLock)
        {
            return IgnoredJobLinksInRun.Contains(normalizedLink);
        }
    }

    private static string NormalizeLinkedInJobLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return string.Empty;
        }

        try
        {
            var raw = link.Trim();
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            for (var i = 0; i < segments.Count - 2; i++)
            {
                if (!segments[i].Equals("jobs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!segments[i + 1].Equals("view", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var jobId = (segments[i + 2] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(jobId))
                {
                    continue;
                }

                return $"https://www.linkedin.com/jobs/view/{jobId}/";
            }

            var absolutePath = uri.AbsolutePath;
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return string.Empty;
            }

            var normalizedPath = absolutePath.EndsWith("/") ? absolutePath : absolutePath + "/";
            return $"{uri.Scheme}://{uri.Host}{normalizedPath}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? TryGetQueryParamValue(string? url, string key)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var query = (uri.Query ?? string.Empty).TrimStart('?');
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 0)
                {
                    continue;
                }

                var k = Uri.UnescapeDataString(kv[0] ?? string.Empty);
                if (!k.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var v = kv.Length > 1 ? kv[1] : string.Empty;
                return Uri.UnescapeDataString(v ?? string.Empty);
            }
        }
        catch
        {
            // ignora
        }

        return null;
    }

    private static IWebElement? TryFindActiveEasyApplyModal(IWebDriver driver)
    {
        try
        {
            var modals = driver.FindElements(By.CssSelector("div.jobs-easy-apply-modal[role='dialog'], div.jobs-easy-apply-modal, div[role='dialog']"));
            foreach (var modal in modals)
            {
                try
                {
                    if (!modal.Displayed)
                    {
                        continue;
                    }

                    var ariaHidden = modal.GetAttribute("aria-hidden")?.Trim();
                    if (ariaHidden != null && ariaHidden.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return modal;
                }
                catch
                {
                    // Tenta próximo modal.
                }
            }
        }
        catch
        {
            // Ignora falhas de busca.
        }

        return null;
    }

    private static string BuildRequiredFieldsSummary(IWebDriver driver)
    {
        try
        {
            var modal = TryFindActiveEasyApplyModal(driver);
            if (modal == null)
            {
                return "(modal não identificado)";
            }

            var requiredCandidates = modal.FindElements(By.CssSelector(
                "input[required], select[required], textarea[required], " +
                "input[aria-required='true'], select[aria-required='true'], textarea[aria-required='true']"));

            var feedbackCandidates = modal.FindElements(By.CssSelector(
                ".artdeco-inline-feedback__message, .artdeco-inline-feedback, .artdeco-text-input--error, .artdeco-text-input__error"));

            string DescribeField(IWebElement field)
            {
                string Safe(string? s) => (s ?? string.Empty).Trim();

                var tag = Safe(field.TagName).ToLowerInvariant();
                var type = Safe(field.GetAttribute("type")).ToLowerInvariant();
                var name = Safe(field.GetAttribute("name"));
                var id = Safe(field.GetAttribute("id"));
                var ariaLabel = Safe(field.GetAttribute("aria-label"));
                var placeholder = Safe(field.GetAttribute("placeholder"));

                var key = !string.IsNullOrWhiteSpace(ariaLabel) ? ariaLabel
                    : !string.IsNullOrWhiteSpace(placeholder) ? placeholder
                    : !string.IsNullOrWhiteSpace(name) ? name
                    : !string.IsNullOrWhiteSpace(id) ? id
                    : "campo_sem_id";

                return $"{tag}:{type}:{key}";
            }

            bool IsFilled(IWebElement field)
            {
                try
                {
                    if (!field.Displayed)
                    {
                        return true;
                    }

                    var tag = (field.TagName ?? string.Empty).Trim().ToLowerInvariant();
                    var type = (field.GetAttribute("type") ?? string.Empty).Trim().ToLowerInvariant();

                    if (tag == "select")
                    {
                        var value = (field.GetAttribute("value") ?? string.Empty).Trim();
                        return !string.IsNullOrWhiteSpace(value);
                    }

                    if (type == "checkbox" || type == "radio")
                    {
                        var selected = false;
                        try { selected = field.Selected; } catch { selected = false; }
                        return selected;
                    }

                    var v = (field.GetAttribute("value") ?? string.Empty).Trim();
                    return !string.IsNullOrWhiteSpace(v);
                }
                catch
                {
                    return true;
                }
            }

            var missing = new List<string>();
            foreach (var field in requiredCandidates)
            {
                try
                {
                    if (!field.Displayed)
                    {
                        continue;
                    }

                    var disabledAttr = field.GetAttribute("disabled");
                    if (!string.IsNullOrWhiteSpace(disabledAttr))
                    {
                        continue;
                    }

                    if (!IsFilled(field))
                    {
                        missing.Add(DescribeField(field));
                    }
                }
                catch
                {
                    // Ignora campo que não conseguiu ler.
                }
            }

            var messages = new List<string>();
            foreach (var msg in feedbackCandidates)
            {
                try
                {
                    if (!msg.Displayed)
                    {
                        continue;
                    }

                    var text = (msg.Text ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var lower = text.ToLowerInvariant();
                    if (lower.Contains("obrig") || lower.Contains("required") || lower.Contains("preencha") || lower.Contains("please"))
                    {
                        messages.Add(text);
                    }
                }
                catch
                {
                    // Ignora.
                }
            }

            var missingDistinct = missing.Distinct().Take(12).ToList();
            var messagesDistinct = messages.Distinct().Take(6).ToList();

            if (missingDistinct.Count == 0 && messagesDistinct.Count == 0)
            {
                return "(nenhum campo obrigatório vazio detectado automaticamente)";
            }

            var missingPart = missingDistinct.Count > 0 ? $"missing=[{string.Join("; ", missingDistinct)}]" : string.Empty;
            var msgPart = messagesDistinct.Count > 0 ? $"messages=[{string.Join(" | ", messagesDistinct)}]" : string.Empty;

            return (missingPart + (missingPart != string.Empty && msgPart != string.Empty ? ", " : string.Empty) + msgPart).Trim();
        }
        catch (Exception ex)
        {
            return $"(falha ao analisar campos obrigatórios: {ex.Message})";
        }
    }

    private static string DescribeElementForLog(IWebElement element)
    {
        try
        {
            var id = element.GetAttribute("id")?.Trim() ?? string.Empty;
            var cssClass = element.GetAttribute("class")?.Trim() ?? string.Empty;
            var ariaLabel = element.GetAttribute("aria-label")?.Trim() ?? string.Empty;
            var dataNext = element.GetAttribute("data-easy-apply-next-button")?.Trim() ?? string.Empty;
            var dataLiveNext = element.GetAttribute("data-live-test-easy-apply-next-button")?.Trim() ?? string.Empty;
            var text = element.Text?.Trim() ?? string.Empty;

            return $"id='{id}', class='{cssClass}', aria-label='{ariaLabel}', text='{text}', data-easy-apply-next-button='{dataNext}', data-live-test-easy-apply-next-button='{dataLiveNext}'";
        }
        catch
        {
            return "(sem metadados do elemento)";
        }
    }

    private static string SanitizeFileNamePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var sanitized = value;
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }

        return sanitized.Length > 80 ? sanitized.Substring(0, 80) : sanitized;
    }

    private static string GetJobTokenFromLink(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return "job_unknown";
        }

        try
        {
            var uri = new Uri(link);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var lastSegment = segments.Length > 0 ? segments[segments.Length - 1] : "job_unknown";
            return SanitizeFileNamePart(lastSegment);
        }
        catch
        {
            return "job_unknown";
        }
    }

    private static (string? HtmlPath, string? ScreenshotPath) SaveFailureDiagnostics(IWebDriver driver, string link, string stage)
    {
        try
        {
            var diagnosticsDir = Path.Combine(AppContext.BaseDirectory, "diagnostics");
            Directory.CreateDirectory(diagnosticsDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var jobToken = GetJobTokenFromLink(link);
            var stageToken = SanitizeFileNamePart(stage);
            var fileBaseName = $"{timestamp}_{jobToken}_{stageToken}";

            var htmlPath = Path.Combine(diagnosticsDir, fileBaseName + ".html");
            var screenshotPath = Path.Combine(diagnosticsDir, fileBaseName + ".png");

            File.WriteAllText(htmlPath, driver.PageSource);

            if (driver is ITakesScreenshot screenshotDriver)
            {
                var screenshot = screenshotDriver.GetScreenshot();
                screenshot.SaveAsFile(screenshotPath);
            }

            Console.WriteLine($"Diagnóstico salvo em: {htmlPath}");
            return (htmlPath, screenshotPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Falha ao salvar diagnóstico: {ex.Message}");
            return (null, null);
        }
    }
}
