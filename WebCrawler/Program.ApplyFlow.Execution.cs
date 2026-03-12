using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

partial class Program
{
    private static bool IsBudgetExhaustedJobLink(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return false;
        }

        var ebp = TryGetQueryParamValue(link, "eBP") ?? string.Empty;
        return ebp.Equals("BUDGET_EXHAUSTED_JOB", StringComparison.OrdinalIgnoreCase);
    }

    // Executa fluxo completo sem perguntas obrigatórias: Candidatura simplificada -> Avançar -> selecionar currículo -> Avançar -> Enviar candidatura.
    private static bool TryCompleteEasyApplyWithoutQuestions(IWebDriver driver, WebDriverWait wait, string link)
    {
        Console.WriteLine("[ETAPA] Procurando botão de candidatura simplificada...");
        LogApplicationStep(link, "flow_started", true, "Início do fluxo Easy Apply.");

        bool HasVisibleSubmitButton()
        {
            try
            {
                var modal = TryFindActiveEasyApplyModal(driver);
                ISearchContext root = modal != null ? (ISearchContext)modal : driver;
                var candidates = root.FindElements(By.CssSelector(
                    "button[data-live-test-easy-apply-submit-button], " +
                    "button[data-easy-apply-submit-button], " +
                    "button[aria-label*='Enviar'], " +
                    "button[aria-label*='Submit']"
                ));

                return candidates.Any(btn =>
                {
                    try
                    {
                        if (!btn.Displayed)
                        {
                            return false;
                        }

                        var text = (btn.Text ?? string.Empty).Trim();
                        var aria = (btn.GetAttribute("aria-label") ?? string.Empty).Trim();
                        var combined = (text + " " + aria).ToLowerInvariant();
                        return combined.Contains("enviar") || combined.Contains("submit");
                    }
                    catch
                    {
                        return false;
                    }
                });
            }
            catch
            {
                return false;
            }
        }

        bool HasVisibleReviewButton()
        {
            try
            {
                var modal = TryFindActiveEasyApplyModal(driver);
                ISearchContext root = modal != null ? (ISearchContext)modal : driver;
                var candidates = root.FindElements(By.XPath(
                    ".//button[contains(@aria-label,'Revise sua candidatura') or contains(@aria-label,'Review your application') or contains(@aria-label,'Revis') or contains(@aria-label,'Review') or " +
                    ".//span[contains(normalize-space(.),'Revis')] or .//span[contains(normalize-space(.),'Review')]]"
                ));

                return candidates.Any(btn =>
                {
                    try { return btn.Displayed; } catch { return false; }
                });
            }
            catch
            {
                return false;
            }
        }

        bool HasVisibleNextButton()
        {
            try
            {
                var modal = TryFindActiveEasyApplyModal(driver);
                ISearchContext root = modal != null ? (ISearchContext)modal : driver;
                var candidates = root.FindElements(By.CssSelector(
                    "button[data-easy-apply-next-button], " +
                    "button[data-live-test-easy-apply-next-button], " +
                    "footer button, " +
                    "button.artdeco-button--primary"
                ));

                foreach (var btn in candidates)
                {
                    try
                    {
                        if (!btn.Displayed || !btn.Enabled)
                        {
                            continue;
                        }

                        var text = (btn.Text ?? string.Empty).Trim();
                        var aria = (btn.GetAttribute("aria-label") ?? string.Empty).Trim();
                        var combined = (text + " " + aria).ToLowerInvariant();

                        var looksLikeNext = combined.Contains("avançar") ||
                                            combined.Contains("próximo") ||
                                            combined.Contains("proximo") ||
                                            combined.Contains("next") ||
                                            combined.Contains("continuar") ||
                                            combined.Contains("continue") ||
                                            combined.Contains("prosseguir");

                        var looksLikeSubmitOrReview = combined.Contains("enviar") ||
                                                      combined.Contains("submit") ||
                                                      combined.Contains("revis") ||
                                                      combined.Contains("review");

                        if (looksLikeNext && !looksLikeSubmitOrReview)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Tenta próximo botão.
                    }
                }
            }
            catch
            {
                // Ignora e considera que não há botão de próximo.
            }

            return false;
        }

        bool TryHandleMandatoryGate(string stageContext)
        {
            if (TryDetectScreeningQuestionsGate(driver, out var screeningGateDetails))
            {
                Console.WriteLine("[ETAPA] Questionário obrigatório detectado. Pulando vaga por enquanto.");
                Console.WriteLine($"[DEBUG SCREENING] {screeningGateDetails}");
                LogApplicationStep(link, $"screening_questions_detected_{stageContext}", false, screeningGateDetails);
                IgnoreJobLinkInCurrentRun(link, "REQUIRES_SELECT_QUESTIONNAIRE");
                MarkJobAsUnavailable(link, "REQUIRES_SCREENING_QUESTIONS");
                TryDismissEasyApplyModal(driver, link, $"screening_{stageContext}");
                return true;
            }

            var requiredSummary = BuildRequiredFieldsSummary(driver);
            var hasRequiredSignals =
                !string.IsNullOrWhiteSpace(requiredSummary) &&
                !requiredSummary.Contains("(nenhum campo obrigatório vazio detectado automaticamente)", StringComparison.OrdinalIgnoreCase) &&
                !requiredSummary.Contains("(modal não identificado)", StringComparison.OrdinalIgnoreCase);

            if (!hasRequiredSignals)
            {
                return false;
            }

            Console.WriteLine("[ETAPA] Campos obrigatórios detectados. Pulando vaga por enquanto.");
            Console.WriteLine($"[DEBUG REQUIRED] {requiredSummary}");
            LogApplicationStep(link, $"mandatory_fields_detected_{stageContext}", false, requiredSummary);
            IgnoreJobLinkInCurrentRun(link, "REQUIRES_MANDATORY_FIELDS");
            MarkJobAsUnavailable(link, "REQUIRES_MANDATORY_FIELDS");
            TryDismissEasyApplyModal(driver, link, $"mandatory_fields_{stageContext}");
            return true;
        }

        // Tenta encontrar o botão principal de candidatura simplificada por id.
        IWebElement? applyButton = null;
        try
        {
            applyButton = wait.Until(d => d.FindElement(By.Id("jobs-apply-button-id")));
        }
        catch (WebDriverTimeoutException)
        {
            // Fallbacks: variantes de layout/idioma para Easy Apply.
            var candidates = driver.FindElements(By.CssSelector(
                "#jobs-apply-button-id, " +
                "button.jobs-apply-button, a.jobs-apply-button, " +
                "button[data-live-test-job-apply-button], a[data-live-test-job-apply-button], " +
                "a[data-view-name='job-apply-button'], button[data-view-name='job-apply-button'], " +
                "a[href*='/apply/?openSDUIApplyFlow=true']"
            ));

            if (candidates.Count == 0)
            {
                // Fallback por texto/aria-label em PT/EN para cenários sem atributos estáveis.
                candidates = driver.FindElements(By.XPath(
                    "//a[contains(@aria-label,'candidatura simplificada') or contains(@aria-label,'Easy Apply') or " +
                    "contains(normalize-space(.),'Candidatura simplificada') or contains(normalize-space(.),'Easy Apply')] | " +
                    "//button[contains(@aria-label,'candidatura simplificada') or contains(@aria-label,'Easy Apply') or " +
                    "contains(normalize-space(.),'Candidatura simplificada') or contains(normalize-space(.),'Easy Apply')]"));
            }

            if (candidates.Count > 0)
            {
                applyButton = candidates[0];
            }
        }

        if (applyButton == null)
        {
            Console.WriteLine("Botão de candidatura simplificada não encontrado nesta vaga.");
            var diagnostics = SaveFailureDiagnostics(driver, link, "easy_apply_not_found");
            LogApplicationStep(link, "easy_apply_not_found", false, "Botão inicial Easy Apply não encontrado.", diagnostics.HtmlPath, diagnostics.ScreenshotPath);
            return false;
        }

        // Scroll até o botão para garantir visibilidade/interação.
        TryScrollElementIntoViewHumanized(driver, applyButton);

        // Regra importante: para manter o comportamento antigo (abrir popup/modal), tentamos SEMPRE clicar primeiro.
        // Alguns fluxos SDUI são links; nesses casos, se o clique não disparar nada, usamos fallback de navegar pelo href.
        var applyHref = (applyButton.GetAttribute("href") ?? string.Empty).Trim();
        var isSduiApplyHref = !string.IsNullOrWhiteSpace(applyHref) &&
            applyHref.Contains("/apply/?openSDUIApplyFlow=true", StringComparison.OrdinalIgnoreCase);
        var ebp = isSduiApplyHref ? (TryGetQueryParamValue(applyHref, "eBP") ?? string.Empty) : string.Empty;

        // 1) Clique primeiro (prioriza popup/modal)
        ClickElementRobust(driver, applyButton);
        PauseBetweenFlowSteps();

        // 2) Se o clique não mudou nada e for SDUI, navega direto para o href
        try
        {
            var urlAfterClick = driver.Url ?? string.Empty;
            var hasModalAfterClick = TryFindActiveEasyApplyModal(driver) != null;
            var isAlreadyInApplyUrl = urlAfterClick.Contains("/apply/?openSDUIApplyFlow=true", StringComparison.OrdinalIgnoreCase);

            if (!hasModalAfterClick && !isAlreadyInApplyUrl && isSduiApplyHref)
            {
                if (ebp.Equals("BUDGET_EXHAUSTED_JOB", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Clique não abriu popup e o SDUI href indica BUDGET_EXHAUSTED_JOB (provável candidatura indisponível). Ignorando.");
                    MarkJobAsUnavailable(link, "BUDGET_EXHAUSTED_JOB");
                    var diagnostics = SaveFailureDiagnostics(driver, link, "sdui_apply_blocked_budget_exhausted");
                    LogApplicationStep(link, "sdui_apply_blocked_budget_exhausted", false,
                        $"Clique não abriu popup; SDUI apply href possui eBP=BUDGET_EXHAUSTED_JOB. href='{applyHref}'.",
                        diagnostics.HtmlPath, diagnostics.ScreenshotPath);
                    return false;
                }

                Console.WriteLine($"Clique não abriu popup; usando fallback SDUI navigate para: {applyHref}");
                LogApplicationStep(link, "easy_apply_navigate_sdui_href_fallback", true,
                    $"Clique não abriu popup; navegando para href SDUI apply. eBP='{ebp}'. href='{applyHref}'.");
                driver.Navigate().GoToUrl(applyHref);
                PauseBetweenFlowSteps();
            }
        }
        catch
        {
            // Se falhar leitura de URL/modal, segue fluxo.
        }

        Console.WriteLine("Ação de candidatura simplificada executada (click com fallback navigate quando aplicável).");
        LogApplicationStep(link, "easy_apply_clicked", true, "Ação inicial Easy Apply executada (click-first).");

        // Em vagas SDUI, pode existir um botão/link intermediário "Continuar" antes do popup padrão.
        TryClickContinueEntryIfPresent(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(5)), link);

        // Aguarda estado do fluxo antes de buscar o primeiro botão Avançar.
        if (!WaitForApplyFlowReady(driver, wait, link))
        {
            if (!TryFinalizeWithoutNextIfPossible(driver, wait, link))
            {
                if (TryHandleMandatoryGate("apply_flow_not_ready"))
                {
                    return false;
                }

                return false;
            }

            LogApplicationStep(link, "application_completed_via_not_ready_fallback", true, "Fluxo concluído por fallback após atraso no carregamento do apply.");
            return true;
        }

        // Antes de clicar em Avançar, valida se o popup certo está aberto com as informações de contato.
        if (!VerifyContactPopupBeforeNext(driver, link))
        {
            if (!TryFinalizeWithoutNextIfPossible(driver, wait, link))
            {
                if (TryHandleMandatoryGate("contact_popup_not_verified"))
                {
                    return false;
                }

                return false;
            }

            LogApplicationStep(link, "application_completed_via_popup_check_fallback", true, "Fluxo concluído por fallback após falha na validação do popup de contato.");
            return true;
        }

        // Primeiro avanço no popup após abrir candidatura simplificada.
        Console.WriteLine("[ETAPA] Tentando primeiro 'Avançar'...");
        if (!TryClickNextButtonInPopup(driver, wait, "primeira etapa", link))
        {
            if (!TryFinalizeWithoutNextIfPossible(driver, wait, link))
            {
                if (TryHandleMandatoryGate("first_next"))
                {
                    return false;
                }

                return false;
            }

            LogApplicationStep(link, "application_completed_via_fallback", true, "Fluxo concluído por fallback sem etapa Avançar.");
            return true;
        }

        // Aguarda estabilização real da etapa seguinte.
        if (!WaitForStepTransitionAfterFirstNext(driver, link))
        {
            if (!TryFinalizeWithoutNextIfPossible(driver, wait, link))
            {
                if (TryHandleMandatoryGate("post_first_next_transition"))
                {
                    return false;
                }

                return false;
            }

            LogApplicationStep(link, "application_completed_via_post_first_next_fallback", true, "Fluxo concluído por fallback após transição incerta do primeiro Avançar.");
            return true;
        }

        // Pequena folga adicional para componentes internos do modal renderizarem totalmente.
        PauseBetweenFlowSteps();

        // Seleciona currículo na etapa seguinte quando necessário.
        Console.WriteLine("[ETAPA] Tentando selecionar currículo...");
        TrySelectResumeInPopup(driver);
        Thread.Sleep(350);
        LogApplicationStep(link, "resume_step_processed", true, "Etapa de currículo processada.");

        // Segundo avanço no popup após seleção de currículo.
        Console.WriteLine("[ETAPA] Tentando segundo 'Avançar'...");
        if (!TryClickNextButtonInPopup(driver, wait, "etapa de currículo", link))
        {
            Console.WriteLine("[ETAPA] Segundo 'Avançar' falhou na primeira tentativa; aguardando e tentando novamente...");
            Thread.Sleep(900);
            TrySelectResumeInPopup(driver);
            Thread.Sleep(350);

            if (!TryClickNextButtonInPopup(driver, wait, "etapa de currículo_retry", link))
            {
                if (TryHandleMandatoryGate("resume_next_retry"))
                {
                    return false;
                }

                return false;
            }
        }

        // Algumas vagas (ex.: Screening questions) exigem etapas extras com mais de um botão Avançar.
        const int maxExtraNextSteps = 3;
        for (var extraStep = 1; extraStep <= maxExtraNextSteps; extraStep++)
        {
            if (HasVisibleSubmitButton() || HasVisibleReviewButton())
            {
                break;
            }

            if (!HasVisibleNextButton())
            {
                break;
            }

            Console.WriteLine($"[ETAPA] Detectada etapa adicional. Tentando 'Avançar' extra #{extraStep}...");
            if (!TryClickNextButtonInPopup(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(10)), $"etapa extra {extraStep}", link))
            {
                Console.WriteLine($"[ETAPA] 'Avançar' extra #{extraStep} não pôde ser concluído.");
                break;
            }

            Thread.Sleep(850);
            TrySelectResumeInPopup(driver);
            Thread.Sleep(250);
            LogApplicationStep(link, $"extra_next_step_{extraStep}_processed", true, "Etapa adicional de avanço processada.");
        }

        if (TryDetectScreeningQuestionsGate(driver, out var screeningGateDetailsBeforeReview))
        {
            Console.WriteLine("[ETAPA] Perguntas adicionais obrigatórias detectadas antes de revisar. Tentando preenchimento automático...");
            Console.WriteLine($"[DEBUG SCREENING] {screeningGateDetailsBeforeReview}");

            var autoFilledScreening = TryAutoFillMandatoryFieldsInModal(driver, link, "screening_before_review");
            if (autoFilledScreening)
            {
                Thread.Sleep(700);

                if (!HasVisibleSubmitButton() && HasVisibleNextButton())
                {
                    Console.WriteLine("[AUTO-FILL] Tentando avançar após preencher perguntas obrigatórias.");
                    TryClickNextButtonInPopup(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(10)), "screening_autofill_advance", link);
                    Thread.Sleep(650);
                }

                if (!TryDetectScreeningQuestionsGate(driver, out _))
                {
                    Console.WriteLine("[AUTO-FILL] Gate de questionário obrigatório removido. Continuando fluxo.");
                }
            }

            if (TryDetectScreeningQuestionsGate(driver, out var screeningGateDetailsAfterAutofill))
            {
                Console.WriteLine("[ETAPA] Perguntas obrigatórias ainda bloqueando envio. Pulando vaga por enquanto.");
                Console.WriteLine($"[DEBUG SCREENING] {screeningGateDetailsAfterAutofill}");
                LogApplicationStep(link, "screening_questions_detected_before_review", false, screeningGateDetailsAfterAutofill);
                IgnoreJobLinkInCurrentRun(link, "REQUIRES_SELECT_QUESTIONNAIRE");
                MarkJobAsUnavailable(link, "REQUIRES_SCREENING_QUESTIONS");
                TryDismissEasyApplyModal(driver, link, "screening_before_review");
                return false;
            }
        }

        // Algumas vagas exibem etapa de revisão antes do envio.
        Console.WriteLine("[ETAPA] Tentando etapa 'Revisar' (quando existir)...");
        var reviewed = TryClickReviewButtonInPopup(driver, wait, link);
        if (reviewed)
        {
            // Aguarda transição para a tela de envio após revisão.
            PauseBetweenFlowSteps();
        }

        // Aguarda transição para a etapa final de envio.
        PauseBetweenFlowSteps();

        IWebElement? FindSubmitButtonCandidate()
        {
            try
            {
                var modal = TryFindActiveEasyApplyModal(driver);
                ISearchContext searchRoot = modal != null ? (ISearchContext)modal : driver;

                var submitCandidates = searchRoot.FindElements(By.CssSelector(
                    "button[data-live-test-easy-apply-submit-button], " +
                    "button[data-easy-apply-submit-button], " +
                    "button[aria-label*='Enviar'], " +
                    "button[aria-label*='Submit']"
                ));

                if (submitCandidates.Count == 0)
                {
                    submitCandidates = searchRoot.FindElements(By.XPath(
                        ".//button[" +
                        "contains(@aria-label,'Enviar candidatura') or contains(@aria-label,'Enviar') or contains(@aria-label,'Submit application') or contains(@aria-label,'Submit') or " +
                        ".//span[contains(normalize-space(.),'Enviar candidatura')] or .//span[contains(normalize-space(.),'Enviar')] or " +
                        ".//span[contains(normalize-space(.),'Submit application')] or .//span[contains(normalize-space(.),'Submit')]" +
                        "]"));
                }

                if (submitCandidates.Count == 0)
                {
                    return null;
                }

                return submitCandidates.FirstOrDefault(btn =>
                {
                    try
                    {
                        if (!btn.Displayed)
                        {
                            return false;
                        }

                        var text = (btn.Text ?? string.Empty).Trim();
                        var aria = (btn.GetAttribute("aria-label") ?? string.Empty).Trim();
                        var combined = (text + " " + aria).ToLowerInvariant();

                        return combined.Contains("enviar") || combined.Contains("submit");
                    }
                    catch
                    {
                        return false;
                    }
                }) ?? submitCandidates[0];
            }
            catch
            {
                return null;
            }
        }

        // Tenta localizar botão final de envio da candidatura.
        IWebElement? submitButton = null;
        try
        {
            submitButton = wait.Until(_ => FindSubmitButtonCandidate());
        }
        catch (WebDriverTimeoutException)
        {
            submitButton = FindSubmitButtonCandidate();
        }

        // Se encontrou o botão de envio, mas ele está desabilitado, captura diagnóstico para entender campos obrigatórios.
        if (submitButton != null)
        {
            try
            {
                var ariaDisabled = (submitButton.GetAttribute("aria-disabled") ?? string.Empty).Trim();
                var disabledAttr = submitButton.GetAttribute("disabled") ?? string.Empty;

                if (!submitButton.Displayed)
                {
                    submitButton = null;
                }
                else if (!submitButton.Enabled || ariaDisabled.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryAutoFillMandatoryFieldsInModal(driver, link, "submit_disabled"))
                    {
                        Thread.Sleep(650);
                        var submitAfterAutoFill = FindSubmitButtonCandidate();
                        if (submitAfterAutoFill != null)
                        {
                            try
                            {
                                var ariaDisabledAfterAutoFill = (submitAfterAutoFill.GetAttribute("aria-disabled") ?? string.Empty).Trim();
                                if (submitAfterAutoFill.Displayed && submitAfterAutoFill.Enabled && !ariaDisabledAfterAutoFill.Equals("true", StringComparison.OrdinalIgnoreCase))
                                {
                                    submitButton = submitAfterAutoFill;
                                    Console.WriteLine("[AUTO-FILL] Botão 'Enviar candidatura' reabilitado após preenchimento automático.");
                                    LogApplicationStep(link, "submit_enabled_after_autofill", true, "Botão Enviar ficou habilitado após preenchimento automático de campos obrigatórios.");
                                }
                            }
                            catch
                            {
                                // Se falhar leitura do estado reavaliado, segue validação padrão.
                            }
                        }
                    }

                    var ariaDisabledAfterRetry = (submitButton.GetAttribute("aria-disabled") ?? string.Empty).Trim();
                    if (submitButton.Displayed && submitButton.Enabled && !ariaDisabledAfterRetry.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        // Submit foi habilitado após preenchimento automático; segue fluxo de envio.
                    }
                    else
                    {
                    var submitDescription = DescribeElementForLog(submitButton);
                    var requiredSummary = BuildRequiredFieldsSummary(driver);

                    if (TryDetectScreeningQuestionsGate(driver, out var screeningGateDetailsDisabledSubmit))
                    {
                        Console.WriteLine("[ETAPA] Submit desabilitado por questionário obrigatório. Pulando vaga por enquanto.");
                        Console.WriteLine($"[DEBUG SCREENING] {screeningGateDetailsDisabledSubmit}");
                        LogApplicationStep(link, "screening_questions_detected_submit_disabled", false, screeningGateDetailsDisabledSubmit);
                        IgnoreJobLinkInCurrentRun(link, "REQUIRES_SELECT_QUESTIONNAIRE");
                        MarkJobAsUnavailable(link, "REQUIRES_SCREENING_QUESTIONS");
                        TryDismissEasyApplyModal(driver, link, "screening_submit_disabled");
                        return false;
                    }

                    Console.WriteLine("Botão de envio identificado, porém desabilitado (provável etapa/validação obrigatória).");
                    Console.WriteLine($"[DEBUG SUBMIT DISABLED] aria-disabled='{ariaDisabled}', disabledAttr='{disabledAttr}', {submitDescription}");
                    Console.WriteLine($"[DEBUG REQUIRED] {requiredSummary}");

                    var diagnosticsDisabled = SaveFailureDiagnostics(driver, link, "submit_disabled_or_validation");
                    LogApplicationStep(link, "submit_disabled_or_validation", false, $"Botão Enviar/Submit presente porém desabilitado. aria-disabled='{ariaDisabled}', disabled='{disabledAttr}'. {submitDescription}. REQUIRED: {requiredSummary}", diagnosticsDisabled.HtmlPath, diagnosticsDisabled.ScreenshotPath);
                    IgnoreJobLinkInCurrentRun(link, "REQUIRES_MANDATORY_FIELDS");
                    MarkJobAsUnavailable(link, "REQUIRES_MANDATORY_FIELDS");
                    TryDismissEasyApplyModal(driver, link, "mandatory_fields_submit_disabled");
                    return false;
                    }
                }
            }
            catch
            {
                // Se falhar leitura de atributos, segue fluxo normal.
            }
        }

        // Se não encontrou o botão de envio, provavelmente há perguntas/etapas obrigatórias.
        if (submitButton == null)
        {
            var autoFilledBeforeSkip = TryAutoFillMandatoryFieldsInModal(driver, link, "submit_not_found");
            if (autoFilledBeforeSkip)
            {
                Thread.Sleep(650);

                for (var autoFillAdvanceAttempt = 1; autoFillAdvanceAttempt <= 2; autoFillAdvanceAttempt++)
                {
                    if (HasVisibleSubmitButton() || !HasVisibleNextButton())
                    {
                        break;
                    }

                    Console.WriteLine($"[AUTO-FILL] Tentando 'Avançar' após preenchimento automático (tentativa {autoFillAdvanceAttempt}/2)...");
                    if (!TryClickNextButtonInPopup(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(10)), $"autofill_no_submit_{autoFillAdvanceAttempt}", link))
                    {
                        break;
                    }

                    Thread.Sleep(600);
                }

                if (!HasVisibleSubmitButton())
                {
                    TryClickReviewButtonInPopup(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(7)), link);
                    Thread.Sleep(500);
                }

                submitButton = FindSubmitButtonCandidate();
                if (submitButton != null)
                {
                    try
                    {
                        var ariaDisabledAfterAutoFill = (submitButton.GetAttribute("aria-disabled") ?? string.Empty).Trim();
                        if (!submitButton.Displayed || !submitButton.Enabled || ariaDisabledAfterAutoFill.Equals("true", StringComparison.OrdinalIgnoreCase))
                        {
                            submitButton = null;
                        }
                        else
                        {
                            Console.WriteLine("[AUTO-FILL] Botão 'Enviar candidatura' encontrado após preenchimento automático.");
                            LogApplicationStep(link, "submit_found_after_autofill", true, "Botão Enviar encontrado após preenchimento automático de campos obrigatórios.");
                        }
                    }
                    catch
                    {
                        submitButton = null;
                    }
                }
            }
        }

        if (submitButton == null)
        {
            Console.WriteLine("Vaga com etapas obrigatórias adicionais detectada. Ignorando por enquanto.");

            if (TryDetectScreeningQuestionsGate(driver, out var screeningGateDetailsNoSubmit))
            {
                Console.WriteLine("[ETAPA] Questionário obrigatório detectado (sem botão de envio). Pulando vaga por enquanto.");
                Console.WriteLine($"[DEBUG SCREENING] {screeningGateDetailsNoSubmit}");
                LogApplicationStep(link, "screening_questions_detected_no_submit", false, screeningGateDetailsNoSubmit);
                IgnoreJobLinkInCurrentRun(link, "REQUIRES_SELECT_QUESTIONNAIRE");
                MarkJobAsUnavailable(link, "REQUIRES_SCREENING_QUESTIONS");
                TryDismissEasyApplyModal(driver, link, "screening_no_submit");
                return false;
            }

            var requiredSummary = BuildRequiredFieldsSummary(driver);
            Console.WriteLine($"[DEBUG REQUIRED] {requiredSummary}");
            var diagnostics = SaveFailureDiagnostics(driver, link, "submit_not_found_or_required_questions");
            LogApplicationStep(link, "submit_not_found_or_required_questions", false, $"Fluxo exige etapas adicionais/perguntas obrigatórias. REQUIRED: {requiredSummary}", diagnostics.HtmlPath, diagnostics.ScreenshotPath);
            IgnoreJobLinkInCurrentRun(link, "REQUIRES_MANDATORY_FIELDS");
            MarkJobAsUnavailable(link, "REQUIRES_MANDATORY_FIELDS");
            TryDismissEasyApplyModal(driver, link, "mandatory_fields_no_submit");
            return false;
        }

        // Scroll e clique no botão final para enviar candidatura.
        TryScrollElementIntoViewHumanized(driver, submitButton);
        ClickElementRobust(driver, submitButton);
        Console.WriteLine("Botão 'Enviar candidatura' clicado com sucesso.");
        LogApplicationStep(link, "submit_clicked", true, "Botão Enviar candidatura clicado com sucesso.");

        return true;
    }

    // Abre cada link vindo do banco e executa o fluxo de candidatura simplificada.
    private static void ApplySimplifiedJobsFromDatabase(IWebDriver driver, int maxJobsToProcess = 0)
    {
        try
        {
            // Busca links de vagas simplificadas já persistidas no banco.
            var links = GetSimplifiedJobLinksFromDatabase();
            Console.WriteLine($"Total de links com candidatura simplificada no banco: {links.Count}");

            // Wait usado para localizar o botão de candidatura em cada página.
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(25));

            int processedJobs = 0;

            // Percorre cada URL retornada no SELECT.
            foreach (var link in links)
            {
                if (maxJobsToProcess > 0 && processedJobs >= maxJobsToProcess)
                {
                    Console.WriteLine($"Limite de vagas para candidatura por ciclo atingido ({maxJobsToProcess}).");
                    break;
                }

                if (string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                if (IsIgnoredJobLinkInCurrentRun(link))
                {
                    Console.WriteLine("Pulando vaga já marcada como ignorada nesta execução.");
                    continue;
                }

                if (IsBudgetExhaustedJobLink(link))
                {
                    Console.WriteLine("Pulando vaga com eBP=BUDGET_EXHAUSTED_JOB (candidatura indisponível).");
                    MarkJobAsUnavailable(link, "BUDGET_EXHAUSTED_JOB");
                    LogApplicationStep(link, "job_skipped_budget_exhausted", true, "Vaga ignorada por eBP=BUDGET_EXHAUSTED_JOB antes da navegação.");
                    continue;
                }

                processedJobs++;

                try
                {
                    Console.WriteLine($"Abrindo vaga do banco: {link}");
                    LogApplicationStep(link, "job_open_started", true, "Abrindo link da vaga.");

                    // Navega para a página da vaga.
                    driver.Navigate().GoToUrl(link);

                    // Aguarda carregamento e estabilização da página da vaga.
                    WaitForJobPageReady(driver);
                    wait.Until(d => d.FindElement(By.TagName("body")));

                    // Executa fluxo completo até o envio final para vagas sem perguntas obrigatórias.
                    var flowCompleted = TryCompleteEasyApplyWithoutQuestions(driver, wait, link);
                    if (!flowCompleted)
                    {
                        Console.WriteLine("Fluxo de candidatura não concluído para esta vaga (ignorando e seguindo para a próxima).");
                        PauseBetweenApplications(link, flowCompleted: false);
                        continue;
                    }

                    // Marca no banco somente quando o fluxo do popup foi concluído.
                    var appliedMarkedInDatabase = MarkJobAsApplied(link);
                    RegisterSuccessfulApplication(link, "DATABASE_FLOW");

                    if (appliedMarkedInDatabase)
                    {
                        Console.WriteLine("Vaga marcada como candidatura_enviada = TRUE no banco.");
                        LogApplicationStep(link, "job_marked_as_applied", true, "Vaga marcada como candidatura enviada.");
                    }
                    else
                    {
                        Console.WriteLine("[ALERTA] Candidatura enviada, mas nenhuma linha foi atualizada no banco para candidatura_enviada.");
                        LogApplicationStep(link, "job_marked_as_applied", false, "Candidatura enviada no site, mas nenhuma linha foi encontrada para atualizar candidatura_enviada.");
                    }

                    PauseBetweenApplications(link, flowCompleted: true);
                }
                catch (WebDriverTimeoutException ex)
                {
                    Console.WriteLine($"Timeout ao processar vaga {link}: {ex.Message}");
                    var diagnostics = SaveFailureDiagnostics(driver, link, "job_processing_timeout");
                    LogApplicationStep(link, "job_processing_timeout", false, ex.Message, diagnostics.HtmlPath, diagnostics.ScreenshotPath);
                    continue;
                }
                catch (StaleElementReferenceException ex)
                {
                    Console.WriteLine($"Elemento stale ao processar vaga {link}: {ex.Message}");
                    var diagnostics = SaveFailureDiagnostics(driver, link, "job_processing_stale_element");
                    LogApplicationStep(link, "job_processing_stale_element", false, ex.Message, diagnostics.HtmlPath, diagnostics.ScreenshotPath);
                    continue;
                }
                catch (NoSuchWindowException ex)
                {
                    Console.WriteLine($"Janela do navegador indisponível ao processar vaga {link}: {ex.Message}");
                    LogApplicationStep(link, "job_processing_no_window", false, ex.Message);
                    break;
                }
                catch (WebDriverException ex)
                {
                    Console.WriteLine($"Erro de WebDriver ao processar vaga {link}: {ex.Message}");
                    var diagnostics = SaveFailureDiagnostics(driver, link, "job_processing_webdriver_exception");
                    LogApplicationStep(link, "job_processing_webdriver_exception", false, ex.Message, diagnostics.HtmlPath, diagnostics.ScreenshotPath);
                    continue;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao processar vaga {link}: {ex.Message}");
                    var diagnostics = SaveFailureDiagnostics(driver, link, "job_processing_exception");
                    LogApplicationStep(link, "job_processing_exception", false, ex.Message, diagnostics.HtmlPath, diagnostics.ScreenshotPath);
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            // Loga erro geral da etapa de candidatura automática por links do banco.
            Console.WriteLine($"Erro na rotina de candidatura por banco: {ex.Message}");
        }
    }

    // Executa candidatura direta a partir dos links coletados no ciclo atual (sem usar banco).
    private static void ApplySimplifiedJobsFromLinks(IWebDriver driver, List<string> links, int maxJobsToProcess = 0)
    {
        try
        {
            Console.WriteLine($"Total de links para candidatura direta: {links.Count}");
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(25));
            int processedJobs = 0;

            foreach (var link in links)
            {
                if (maxJobsToProcess > 0 && processedJobs >= maxJobsToProcess)
                {
                    Console.WriteLine($"Limite de vagas para candidatura por ciclo atingido ({maxJobsToProcess}).");
                    break;
                }

                if (string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                if (IsIgnoredJobLinkInCurrentRun(link))
                {
                    Console.WriteLine("Pulando vaga já marcada como ignorada nesta execução.");
                    continue;
                }

                if (IsBudgetExhaustedJobLink(link))
                {
                    Console.WriteLine("Pulando vaga com eBP=BUDGET_EXHAUSTED_JOB (candidatura indisponível).");
                    MarkJobAsUnavailable(link, "BUDGET_EXHAUSTED_JOB");
                    LogApplicationStep(link, "job_skipped_budget_exhausted", true, "Vaga ignorada por eBP=BUDGET_EXHAUSTED_JOB antes da navegação.");
                    continue;
                }

                processedJobs++;

                try
                {
                    Console.WriteLine($"Abrindo vaga da lista direta: {link}");
                    driver.Navigate().GoToUrl(link);
                    WaitForJobPageReady(driver);
                    wait.Until(d => d.FindElement(By.TagName("body")));

                    var flowCompleted = TryCompleteEasyApplyWithoutQuestions(driver, wait, link);
                    if (!flowCompleted)
                    {
                        Console.WriteLine("Fluxo de candidatura não concluído para esta vaga (seguindo para a próxima).");
                        PauseBetweenApplications(link, flowCompleted: false);
                        continue;
                    }

                    RegisterSuccessfulApplication(link, "DIRECT_LIST_FLOW");
                    Console.WriteLine("Fluxo concluído para a vaga na lista direta.");
                    PauseBetweenApplications(link, flowCompleted: true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao processar vaga (lista direta) {link}: {ex.Message}");
                    var diagnostics = SaveFailureDiagnostics(driver, link, "job_processing_exception_direct_list");
                    LogApplicationStep(link, "job_processing_exception_direct_list", false, ex.Message, diagnostics.HtmlPath, diagnostics.ScreenshotPath);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro na rotina de candidatura direta por lista: {ex.Message}");
        }
    }
}
