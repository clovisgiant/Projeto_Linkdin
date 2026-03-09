using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

partial class Program
{
    private static bool TryAutoFillMandatoryFieldsInModal(IWebDriver driver, string link, string context)
    {
        if (!AutoFillMandatoryFieldsEnabled)
        {
            return false;
        }

        try
        {
            var modal = TryFindActiveEasyApplyModal(driver);
            if (modal == null)
            {
                return false;
            }

            var requiredFields = modal.FindElements(By.CssSelector(
                "input[required], select[required], textarea[required], " +
                "input[aria-required='true'], select[aria-required='true'], textarea[aria-required='true']"));

            if (requiredFields.Count == 0)
            {
                return false;
            }

            var updatedFields = 0;
            var updatedKeys = new List<string>();

            foreach (var field in requiredFields)
            {
                try
                {
                    if (!field.Displayed || IsElementDisabledOrReadonly(field))
                    {
                        continue;
                    }

                    var tag = (field.TagName ?? string.Empty).Trim().ToLowerInvariant();
                    var type = (field.GetAttribute("type") ?? string.Empty).Trim().ToLowerInvariant();
                    var hints = BuildFieldHints(field, modal);
                    var fieldKey = BuildFieldKey(field, tag, type, hints);

                    if (tag == "select")
                    {
                        if (TryFillRequiredSelectField(field, hints))
                        {
                            updatedFields++;
                            updatedKeys.Add(fieldKey);
                        }

                        continue;
                    }

                    if (type == "radio" || type == "checkbox")
                    {
                        if (TrySetRequiredBooleanField(driver, field, modal, hints))
                        {
                            updatedFields++;
                            updatedKeys.Add(fieldKey);
                        }

                        continue;
                    }

                    if (type == "hidden" ||
                        type == "file" ||
                        type == "submit" ||
                        type == "button")
                    {
                        continue;
                    }

                    if (!IsFieldValueEmpty(field))
                    {
                        continue;
                    }

                    var value = ResolveMandatoryFieldValue(hints, tag, type);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (TrySetInputValue(driver, field, value))
                    {
                        updatedFields++;
                        updatedKeys.Add(fieldKey);
                    }
                }
                catch
                {
                    // Ignora campo pontual e segue tentativa nos demais obrigatórios.
                }
            }

            if (updatedFields <= 0)
            {
                return false;
            }

            Thread.Sleep(350);

            var keySummary = string.Join("; ", updatedKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10));

            Console.WriteLine($"[AUTO-FILL] Campos obrigatórios preenchidos automaticamente ({updatedFields}) em '{context}'.");
            LogApplicationStep(link,
                $"mandatory_fields_autofilled_{context}",
                true,
                $"Campos obrigatórios preenchidos automaticamente ({updatedFields}). Campos: {keySummary}");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUTO-FILL] Falha ao tentar preencher campos obrigatórios: {ex.Message}");
            return false;
        }
    }

    private static bool TryFillRequiredSelectField(IWebElement selectField, string hints)
    {
        try
        {
            var selectElement = new SelectElement(selectField);
            var currentOption = selectElement.SelectedOption;

            if (currentOption != null)
            {
                var currentText = currentOption.Text ?? string.Empty;
                var currentValue = currentOption.GetAttribute("value") ?? string.Empty;
                if (!IsPlaceholderOption(currentText, currentValue))
                {
                    return false;
                }
            }

            var candidates = selectElement.Options
                .Where(opt =>
                {
                    try
                    {
                        var disabledAttr = opt.GetAttribute("disabled") ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(disabledAttr))
                        {
                            return false;
                        }

                        var text = (opt.Text ?? string.Empty).Trim();
                        var value = (opt.GetAttribute("value") ?? string.Empty).Trim();
                        return !IsPlaceholderOption(text, value);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();

            if (candidates.Count == 0)
            {
                return false;
            }

            var normalizedHints = NormalizeForMatch(hints);
            IWebElement? chosen = null;

            if (IsSensitiveDisclosureField(normalizedHints))
            {
                chosen = FindOptionContaining(candidates, "prefiro nao", "prefer not", "nao informar", "not say");
            }

            if (chosen == null && IsWorkAuthorizationQuestion(normalizedHints))
            {
                chosen = AutoFillDefaultWorkAuthorization
                    ? FindYesOption(candidates)
                    : FindNoOption(candidates);
            }

            if (chosen == null && IsVisaSponsorshipQuestion(normalizedHints))
            {
                chosen = AutoFillDefaultNeedVisaSponsorship
                    ? FindYesOption(candidates)
                    : FindNoOption(candidates);
            }

            if (chosen == null && HasYesNoOptions(candidates))
            {
                chosen = AutoFillDefaultCheckboxTrue
                    ? FindYesOption(candidates)
                    : FindNoOption(candidates);
            }

            if (chosen == null && IsCountryQuestion(normalizedHints))
            {
                chosen = FindOptionContaining(candidates, "brasil", "brazil", "+55");
            }

            if (chosen == null && IsYearsExperienceQuestion(normalizedHints))
            {
                chosen = FindBestYearsOption(candidates, AutoFillDefaultYearsExperience);
            }

            if (chosen == null)
            {
                chosen = candidates[0];
            }

            if (chosen == null)
            {
                return false;
            }

            var chosenValue = (chosen.GetAttribute("value") ?? string.Empty).Trim();
            var chosenText = (chosen.Text ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(chosenValue))
            {
                selectElement.SelectByValue(chosenValue);
            }
            else
            {
                selectElement.SelectByText(chosenText);
            }

            var selectedNow = selectElement.SelectedOption;
            if (selectedNow == null)
            {
                return false;
            }

            var selectedText = (selectedNow.Text ?? string.Empty).Trim();
            var selectedValue = (selectedNow.GetAttribute("value") ?? string.Empty).Trim();
            return !IsPlaceholderOption(selectedText, selectedValue);
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetRequiredBooleanField(IWebDriver driver, IWebElement field, IWebElement modal, string hints)
    {
        var type = (field.GetAttribute("type") ?? string.Empty).Trim().ToLowerInvariant();
        if (type == "checkbox")
        {
            return TrySetRequiredCheckboxField(driver, field);
        }

        if (type == "radio")
        {
            return TrySetRequiredRadioField(driver, field, modal, hints);
        }

        return false;
    }

    private static bool TrySetRequiredCheckboxField(IWebDriver driver, IWebElement checkbox)
    {
        if (!AutoFillDefaultCheckboxTrue)
        {
            return false;
        }

        try
        {
            if (checkbox.Selected)
            {
                return false;
            }

            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", checkbox);
            Thread.Sleep(120);
            ClickElementRobust(driver, checkbox);
            Thread.Sleep(120);

            if (checkbox.Selected)
            {
                return true;
            }

            ((IJavaScriptExecutor)driver).ExecuteScript(@"
                const el = arguments[0];
                el.checked = true;
                el.dispatchEvent(new Event('input', { bubbles: true }));
                el.dispatchEvent(new Event('change', { bubbles: true }));
            ", checkbox);

            return checkbox.Selected;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetRequiredRadioField(IWebDriver driver, IWebElement radio, IWebElement modal, string hints)
    {
        try
        {
            var fieldName = (radio.GetAttribute("name") ?? string.Empty).Trim();
            var allRadios = modal.FindElements(By.CssSelector("input[type='radio']"));

            var group = allRadios
                .Where(r =>
                {
                    try
                    {
                        if (!r.Displayed || IsElementDisabledOrReadonly(r))
                        {
                            return false;
                        }

                        if (string.IsNullOrWhiteSpace(fieldName))
                        {
                            return true;
                        }

                        var candidateName = (r.GetAttribute("name") ?? string.Empty).Trim();
                        return string.Equals(candidateName, fieldName, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();

            if (group.Count == 0)
            {
                if (!radio.Displayed || IsElementDisabledOrReadonly(radio))
                {
                    return false;
                }

                group.Add(radio);
            }

            if (group.Any(r =>
                {
                    try { return r.Selected; } catch { return false; }
                }))
            {
                return false;
            }

            IWebElement? chosen = null;
            if (AutoFillDefaultCheckboxTrue)
            {
                chosen = group.FirstOrDefault(r => IsYesLikeOption(BuildRadioOptionHints(r, modal)));
            }
            else
            {
                chosen = group.FirstOrDefault(r => IsNoLikeOption(BuildRadioOptionHints(r, modal)));
            }

            chosen ??= group.FirstOrDefault();
            if (chosen == null)
            {
                return false;
            }

            if (TryClickRadioOption(driver, chosen, modal))
            {
                return true;
            }

            // Fallback: tenta no primeiro da lista do grupo.
            var first = group[0];
            return !ReferenceEquals(first, chosen) && TryClickRadioOption(driver, first, modal);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryClickRadioOption(IWebDriver driver, IWebElement radio, IWebElement modal)
    {
        try
        {
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", radio);
            Thread.Sleep(120);
            ClickElementRobust(driver, radio);
            Thread.Sleep(120);

            if (radio.Selected)
            {
                return true;
            }

            var id = (radio.GetAttribute("id") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(id))
            {
                var label = modal.FindElements(By.CssSelector($"label[for='{id}']")).FirstOrDefault();
                if (label != null)
                {
                    try
                    {
                        if (label.Displayed)
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", label);
                            Thread.Sleep(80);
                            ClickElementRobust(driver, label);
                            Thread.Sleep(120);
                        }
                    }
                    catch
                    {
                        // Segue fallback JS.
                    }
                }
            }

            if (radio.Selected)
            {
                return true;
            }

            ((IJavaScriptExecutor)driver).ExecuteScript(@"
                const el = arguments[0];
                el.checked = true;
                el.dispatchEvent(new Event('input', { bubbles: true }));
                el.dispatchEvent(new Event('change', { bubbles: true }));
            ", radio);

            return radio.Selected;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildRadioOptionHints(IWebElement radio, IWebElement modal)
    {
        string Safe(string? value) => (value ?? string.Empty).Trim();

        var tokens = new List<string>
        {
            Safe(radio.GetAttribute("value")),
            Safe(radio.GetAttribute("name")),
            Safe(radio.GetAttribute("aria-label")),
            Safe(radio.GetAttribute("id"))
        };

        var id = Safe(radio.GetAttribute("id"));
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                var label = modal.FindElements(By.CssSelector($"label[for='{id}']")).FirstOrDefault();
                var labelText = (label?.Text ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(labelText))
                {
                    tokens.Add(labelText);
                }
            }
            catch
            {
                // Ignora falha pontual de label.
            }
        }

        try
        {
            var container = radio.FindElement(By.XPath("./ancestor::*[self::div or self::fieldset][1]"));
            var containerText = (container.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(containerText))
            {
                tokens.Add(containerText.Length > 140 ? containerText.Substring(0, 140) : containerText);
            }
        }
        catch
        {
            // Ignora ausência de container.
        }

        return NormalizeForMatch(string.Join(" ", tokens.Where(t => !string.IsNullOrWhiteSpace(t))));
    }

    private static bool IsYesLikeOption(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return false;
        }

        return normalizedText.Contains(" sim", StringComparison.Ordinal) ||
               normalizedText.StartsWith("sim", StringComparison.Ordinal) ||
               normalizedText.Contains(" yes", StringComparison.Ordinal) ||
               normalizedText.StartsWith("yes", StringComparison.Ordinal) ||
               normalizedText.Contains(" true", StringComparison.Ordinal) ||
               normalizedText.StartsWith("true", StringComparison.Ordinal) ||
               normalizedText.Contains(" concordo", StringComparison.Ordinal) ||
               normalizedText.Contains("aceito", StringComparison.Ordinal) ||
               normalizedText.Contains("autorizo", StringComparison.Ordinal);
    }

    private static bool IsNoLikeOption(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return false;
        }

        return normalizedText.Contains(" nao", StringComparison.Ordinal) ||
               normalizedText.StartsWith("nao", StringComparison.Ordinal) ||
               normalizedText.Contains(" no", StringComparison.Ordinal) ||
               normalizedText.StartsWith("no", StringComparison.Ordinal) ||
               normalizedText.Contains(" false", StringComparison.Ordinal) ||
               normalizedText.StartsWith("false", StringComparison.Ordinal);
    }

    private static bool HasYesNoOptions(List<IWebElement> options)
    {
        return FindYesOption(options) != null && FindNoOption(options) != null;
    }

    private static IWebElement? FindBestYearsOption(List<IWebElement> options, int targetYears)
    {
        var normalizedTarget = targetYears <= 0 ? 1 : targetYears;
        IWebElement? fallbackNumeric = null;

        foreach (var option in options)
        {
            try
            {
                var normalized = NormalizeForMatch(option.Text ?? string.Empty);
                var digits = new string(normalized.Where(char.IsDigit).ToArray());
                if (string.IsNullOrWhiteSpace(digits))
                {
                    continue;
                }

                if (!int.TryParse(digits, out var parsedYears))
                {
                    continue;
                }

                fallbackNumeric ??= option;
                if (parsedYears >= normalizedTarget)
                {
                    return option;
                }
            }
            catch
            {
                // Ignora opção inválida.
            }
        }

        return fallbackNumeric;
    }

    private static IWebElement? FindOptionContaining(List<IWebElement> options, params string[] tokens)
    {
        if (tokens == null || tokens.Length == 0)
        {
            return null;
        }

        var normalizedTokens = tokens
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(NormalizeForMatch)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (normalizedTokens.Count == 0)
        {
            return null;
        }

        foreach (var option in options)
        {
            try
            {
                var text = NormalizeForMatch(option.Text ?? string.Empty);
                if (normalizedTokens.Any(token => text.Contains(token, StringComparison.Ordinal)))
                {
                    return option;
                }
            }
            catch
            {
                // Ignora opção inválida.
            }
        }

        return null;
    }

    private static IWebElement? FindYesOption(List<IWebElement> options)
    {
        foreach (var option in options)
        {
            try
            {
                var normalized = NormalizeForMatch(option.Text ?? string.Empty);
                if (normalized == "sim" ||
                    normalized == "yes" ||
                    normalized == "true" ||
                    normalized.StartsWith("sim ", StringComparison.Ordinal) ||
                    normalized.StartsWith("yes ", StringComparison.Ordinal) ||
                    normalized.StartsWith("true ", StringComparison.Ordinal))
                {
                    return option;
                }
            }
            catch
            {
                // Ignora opção inválida.
            }
        }

        return null;
    }

    private static IWebElement? FindNoOption(List<IWebElement> options)
    {
        foreach (var option in options)
        {
            try
            {
                var normalized = NormalizeForMatch(option.Text ?? string.Empty);
                if (normalized == "nao" ||
                    normalized == "não" ||
                    normalized == "no" ||
                    normalized == "false" ||
                    normalized.StartsWith("nao ", StringComparison.Ordinal) ||
                    normalized.StartsWith("não ", StringComparison.Ordinal) ||
                    normalized.StartsWith("no ", StringComparison.Ordinal) ||
                    normalized.StartsWith("false ", StringComparison.Ordinal))
                {
                    return option;
                }
            }
            catch
            {
                // Ignora opção inválida.
            }
        }

        return null;
    }

    private static bool IsPlaceholderOption(string text, string value)
    {
        var normalizedText = NormalizeForMatch(text);
        var normalizedValue = NormalizeForMatch(value);

        if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(normalizedValue))
        {
            return true;
        }

        return normalizedText.Contains("select an option", StringComparison.Ordinal) ||
               normalizedText.Contains("please make a selection", StringComparison.Ordinal) ||
               normalizedText.Contains("selecione", StringComparison.Ordinal) ||
               normalizedText.Contains("escolha", StringComparison.Ordinal) ||
               normalizedText.Contains("choose", StringComparison.Ordinal) ||
               normalizedText.Contains("selecionar", StringComparison.Ordinal) ||
               normalizedValue == "";
    }

    private static string ResolveMandatoryFieldValue(string hints, string tag, string type)
    {
        var normalizedHints = NormalizeForMatch(hints);

        if (type == "email" || normalizedHints.Contains("email", StringComparison.Ordinal) || normalizedHints.Contains("e-mail", StringComparison.Ordinal))
        {
            return AutoFillDefaultEmail;
        }

        if (type == "tel" ||
            normalizedHints.Contains("telefone", StringComparison.Ordinal) ||
            normalizedHints.Contains("celular", StringComparison.Ordinal) ||
            normalizedHints.Contains("phone", StringComparison.Ordinal) ||
            normalizedHints.Contains("mobile", StringComparison.Ordinal))
        {
            return AutoFillDefaultPhone;
        }

        if (normalizedHints.Contains("last name", StringComparison.Ordinal) ||
            normalizedHints.Contains("surname", StringComparison.Ordinal) ||
            normalizedHints.Contains("sobrenome", StringComparison.Ordinal))
        {
            return AutoFillDefaultLastName;
        }

        if (normalizedHints.Contains("first name", StringComparison.Ordinal) ||
            normalizedHints.Contains("nome", StringComparison.Ordinal))
        {
            if (!normalizedHints.Contains("sobrenome", StringComparison.Ordinal) &&
                !normalizedHints.Contains("last", StringComparison.Ordinal) &&
                !normalizedHints.Contains("surname", StringComparison.Ordinal))
            {
                return AutoFillDefaultFirstName;
            }
        }

        if (IsSalaryQuestion(normalizedHints))
        {
            return AutoFillDefaultSalary;
        }

        if (IsYearsExperienceQuestion(normalizedHints))
        {
            return Math.Max(1, AutoFillDefaultYearsExperience).ToString();
        }

        if (type == "number")
        {
            return Math.Max(1, AutoFillDefaultYearsExperience).ToString();
        }

        if (type == "url" ||
            normalizedHints.Contains("website", StringComparison.Ordinal) ||
            normalizedHints.Contains("portfolio", StringComparison.Ordinal) ||
            normalizedHints.Contains("site", StringComparison.Ordinal) ||
            normalizedHints.Contains("url", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(AutoFillDefaultWebsite))
            {
                return AutoFillDefaultWebsite;
            }

            if (!string.IsNullOrWhiteSpace(AutoFillDefaultLinkedIn))
            {
                return AutoFillDefaultLinkedIn;
            }

            return string.Empty;
        }

        if (normalizedHints.Contains("linkedin", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(AutoFillDefaultLinkedIn))
            {
                return AutoFillDefaultLinkedIn;
            }

            if (!string.IsNullOrWhiteSpace(AutoFillDefaultWebsite))
            {
                return AutoFillDefaultWebsite;
            }

            return string.Empty;
        }

        if (normalizedHints.Contains("github", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(AutoFillDefaultGithub))
            {
                return AutoFillDefaultGithub;
            }

            return string.Empty;
        }

        if (normalizedHints.Contains("location", StringComparison.Ordinal) ||
            normalizedHints.Contains("localizacao", StringComparison.Ordinal) ||
            normalizedHints.Contains("localização", StringComparison.Ordinal) ||
            normalizedHints.Contains("cidade", StringComparison.Ordinal) ||
            normalizedHints.Contains("city", StringComparison.Ordinal))
        {
            return AutoFillDefaultLocation;
        }

        if (tag == "textarea" ||
            normalizedHints.Contains("cover letter", StringComparison.Ordinal) ||
            normalizedHints.Contains("carta", StringComparison.Ordinal) ||
            normalizedHints.Contains("mensagem", StringComparison.Ordinal) ||
            normalizedHints.Contains("summary", StringComparison.Ordinal) ||
            normalizedHints.Contains("sobre", StringComparison.Ordinal))
        {
            return AutoFillDefaultGenericText;
        }

        return string.Empty;
    }

    private static bool TrySetInputValue(IWebDriver driver, IWebElement field, string value)
    {
        try
        {
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", field);
            Thread.Sleep(120);

            field.Clear();
            field.SendKeys(value);

            var finalValue = (field.GetAttribute("value") ?? string.Empty).Trim();
            if (string.Equals(finalValue, value, StringComparison.Ordinal))
            {
                return true;
            }

            ((IJavaScriptExecutor)driver).ExecuteScript(@"
                const el = arguments[0];
                const v = arguments[1];
                el.value = v;
                el.dispatchEvent(new Event('input', { bubbles: true }));
                el.dispatchEvent(new Event('change', { bubbles: true }));
            ", field, value);

            finalValue = (field.GetAttribute("value") ?? string.Empty).Trim();
            return string.Equals(finalValue, value, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildFieldHints(IWebElement field, IWebElement modal)
    {
        string Safe(string? value) => (value ?? string.Empty).Trim();

        var id = Safe(field.GetAttribute("id"));
        var name = Safe(field.GetAttribute("name"));
        var ariaLabel = Safe(field.GetAttribute("aria-label"));
        var placeholder = Safe(field.GetAttribute("placeholder"));
        var dataTest = Safe(field.GetAttribute("data-test-text-entity-list-form-component"));
        var dataControlName = Safe(field.GetAttribute("data-control-name"));

        var tokens = new List<string>
        {
            ariaLabel,
            placeholder,
            name,
            id,
            dataTest,
            dataControlName
        };

        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                var label = modal.FindElements(By.CssSelector($"label[for='{id}']")).FirstOrDefault();
                var labelText = (label?.Text ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(labelText))
                {
                    tokens.Add(labelText);
                }
            }
            catch
            {
                // Ignora falha de busca de label por for=id.
            }
        }

        try
        {
            var container = field.FindElement(By.XPath("./ancestor::*[self::div or self::fieldset][1]"));
            var containerText = (container.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(containerText))
            {
                tokens.Add(containerText.Length > 180 ? containerText.Substring(0, 180) : containerText);
            }
        }
        catch
        {
            // Ignora ausência de container compatível.
        }

        return string.Join(" ", tokens.Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    private static string BuildFieldKey(IWebElement field, string tag, string type, string hints)
    {
        string Safe(string? value) => (value ?? string.Empty).Trim();

        var id = Safe(field.GetAttribute("id"));
        var name = Safe(field.GetAttribute("name"));
        var ariaLabel = Safe(field.GetAttribute("aria-label"));
        var placeholder = Safe(field.GetAttribute("placeholder"));

        var key = !string.IsNullOrWhiteSpace(ariaLabel) ? ariaLabel
            : !string.IsNullOrWhiteSpace(placeholder) ? placeholder
            : !string.IsNullOrWhiteSpace(name) ? name
            : !string.IsNullOrWhiteSpace(id) ? id
            : hints;

        key = key.Trim();
        if (key.Length > 80)
        {
            key = key.Substring(0, 80);
        }

        return $"{tag}:{type}:{key}";
    }

    private static bool IsElementDisabledOrReadonly(IWebElement field)
    {
        try
        {
            var disabled = field.GetAttribute("disabled") ?? string.Empty;
            var readOnly = field.GetAttribute("readonly") ?? string.Empty;
            var ariaDisabled = (field.GetAttribute("aria-disabled") ?? string.Empty).Trim();

            return !string.IsNullOrWhiteSpace(disabled) ||
                   !string.IsNullOrWhiteSpace(readOnly) ||
                   ariaDisabled.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsFieldValueEmpty(IWebElement field)
    {
        try
        {
            var value = (field.GetAttribute("value") ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsWorkAuthorizationQuestion(string normalizedHints)
    {
        return normalizedHints.Contains("authorized", StringComparison.Ordinal) ||
               normalizedHints.Contains("autoriza", StringComparison.Ordinal) ||
               normalizedHints.Contains("work permit", StringComparison.Ordinal) ||
               normalizedHints.Contains("direito de trabalhar", StringComparison.Ordinal);
    }

    private static bool IsVisaSponsorshipQuestion(string normalizedHints)
    {
        return normalizedHints.Contains("sponsor", StringComparison.Ordinal) ||
               normalizedHints.Contains("sponsorship", StringComparison.Ordinal) ||
               normalizedHints.Contains("patrocin", StringComparison.Ordinal) ||
               normalizedHints.Contains("visa", StringComparison.Ordinal);
    }

    private static bool IsCountryQuestion(string normalizedHints)
    {
        return normalizedHints.Contains("country", StringComparison.Ordinal) ||
               normalizedHints.Contains("pais", StringComparison.Ordinal) ||
               normalizedHints.Contains("país", StringComparison.Ordinal) ||
               normalizedHints.Contains("codigo do pais", StringComparison.Ordinal) ||
               normalizedHints.Contains("codigo de pais", StringComparison.Ordinal) ||
               normalizedHints.Contains("country code", StringComparison.Ordinal);
    }

    private static bool IsYearsExperienceQuestion(string normalizedHints)
    {
        return normalizedHints.Contains("years", StringComparison.Ordinal) ||
               normalizedHints.Contains("anos", StringComparison.Ordinal) ||
               normalizedHints.Contains("experience", StringComparison.Ordinal) ||
               normalizedHints.Contains("experiencia", StringComparison.Ordinal);
    }

    private static bool IsSalaryQuestion(string normalizedHints)
    {
        return normalizedHints.Contains("salary", StringComparison.Ordinal) ||
               normalizedHints.Contains("salario", StringComparison.Ordinal) ||
               normalizedHints.Contains("pretensao", StringComparison.Ordinal) ||
               normalizedHints.Contains("pretensão", StringComparison.Ordinal) ||
               normalizedHints.Contains("remuneracao", StringComparison.Ordinal) ||
               normalizedHints.Contains("remuneração", StringComparison.Ordinal) ||
               normalizedHints.Contains("compensation", StringComparison.Ordinal) ||
               normalizedHints.Contains("compensacao", StringComparison.Ordinal) ||
               normalizedHints.Contains("compensação", StringComparison.Ordinal) ||
               normalizedHints.Contains("faixa salarial", StringComparison.Ordinal);
    }

    private static bool IsSensitiveDisclosureField(string normalizedHints)
    {
        return normalizedHints.Contains("gender", StringComparison.Ordinal) ||
               normalizedHints.Contains("genero", StringComparison.Ordinal) ||
               normalizedHints.Contains("sexo", StringComparison.Ordinal) ||
               normalizedHints.Contains("race", StringComparison.Ordinal) ||
               normalizedHints.Contains("raca", StringComparison.Ordinal) ||
               normalizedHints.Contains("ethnicity", StringComparison.Ordinal) ||
               normalizedHints.Contains("etnia", StringComparison.Ordinal) ||
               normalizedHints.Contains("veteran", StringComparison.Ordinal) ||
               normalizedHints.Contains("veterano", StringComparison.Ordinal) ||
               normalizedHints.Contains("disability", StringComparison.Ordinal) ||
               normalizedHints.Contains("deficiencia", StringComparison.Ordinal) ||
               normalizedHints.Contains("pcd", StringComparison.Ordinal);
    }

    private static string NormalizeForMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}