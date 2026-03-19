using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

partial class Program
{
    private static bool DatabaseEnabled = true;
    private static bool PersistIgnoredLinksToFile = true;
    private static bool PersistSuccessfulLinksToFile = true;
    private static bool AutoFillMandatoryFieldsEnabled = true;
    private static string AutoFillDefaultFirstName = "Clovis";
    private static string AutoFillDefaultLastName = "Silva";
    private static string AutoFillDefaultPhone = "11959391726";
    private static string AutoFillDefaultLocation = "Sao Paulo";
    private static string AutoFillDefaultEmail = "clovis.eduardosilva23@gmail.com";
    private static string AutoFillDefaultWebsite = "";
    private static string AutoFillDefaultLinkedIn = "www.linkedin.com/in/clovis-silva-dev";
    private static string AutoFillDefaultGithub = "";
    private static string AutoFillDefaultSalary = "7500";
    private static string AutoFillDefaultGenericText = "Tenho experiencia compativel com a vaga e disponibilidade para atuar no escopo solicitado.";
    private static int AutoFillDefaultYearsExperience = 10;
    private static bool AutoFillDefaultCheckboxTrue = true;
    private static bool AutoFillDefaultWorkAuthorization = true;
    private static bool AutoFillDefaultNeedVisaSponsorship = false;
    private static readonly HashSet<string> IgnoredJobLinksInRun = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object IgnoredJobLinksInRunLock = new();
    private static readonly HashSet<string> SuccessfulJobLinksPersisted = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> SuccessfulJobLinksCurrentCycle = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SuccessfulJobLinksLock = new();
    private const string LinkedInFeedUrl = "https://www.linkedin.com/feed/";
    private const string LinkedInLoginUrl = "https://www.linkedin.com/login";
    private const string LinkedInJobsSearchUrl = "https://www.linkedin.com/jobs/search/";
    private const string LinkedInEasyApplyCollectionUrl = "https://www.linkedin.com/jobs/collections/easy-apply/?discover=recommended&discoveryOrigin=JOBS_HOME_JYMBII&start=0";
    private const string JobsOutputFileName = "vagas_linkedin.txt";
    private const string IgnoredJobsFileName = "ignored_job_links.txt";
    private const string SuccessfulJobsCycleFileName = "vagas_enviadas_sucesso.txt";
    private const string SuccessfulJobsHistoryFileName = "vagas_enviadas_sucesso_historico.txt";
    private static readonly object JobSearchTermSelectionLock = new();
    private static int JobSearchTermSelectionIndex;
    private static readonly string[] DefaultJobSearchTerms =
    {
        "Desenvolvedor Backend .NET - Pleno/Senior",
        "Engenharia de Software",
        "Desenvolvedor C#",
        "Analista I de Desenvolvimento de Software",
        "Engenheiro de Software",
        "Analista de Desenvolvimento de Software FullStack",
        "Senior Full Stack Developer | .NET",
        "C#",
        "JavaScript",
        "Python",
        "VB.NET",
        "PHP",
        "VBA",
        "Angular",
        "React",
        "HTML5",
        "CSS3",
        ".NET",
        "APIs REST",
        "Automacao de processos",
        "AWS",
        "EC2",
        "S3",
        "Lambda",
        "Athena",
        "Glue",
        "SQL",
        "PostgreSQL",
        "MySQL",
        "Git",
        "Visual Studio",
        "VS Code",
        "Linux"
    };

    static void Main()
    {
        LoadEnvFileIfExists();

        using var singleInstanceMutex = TryAcquireSingleInstanceMutex();
        if (singleInstanceMutex == null)
        {
            Console.WriteLine("Outra instancia do WebCrawler ja esta em execucao. Encerrando esta inicializacao.");
            return;
        }

        var linkedinUsername = GetRequiredEnv("LINKEDIN_USERNAME");
        var linkedinPassword = GetRequiredEnv("LINKEDIN_PASSWORD");

        var testMode = GetOptionalBoolEnv("WEBCRAWLER_TEST_MODE", false);
        var disableDatabase = GetOptionalBoolEnv("WEBCRAWLER_DISABLE_DATABASE", true);
        var maxPagesPerCycle = GetOptionalIntEnv("WEBCRAWLER_MAX_PAGES_PER_CYCLE", testMode ? 1 : 0);
        var maxJobsToApplyPerCycle = GetOptionalIntEnv("WEBCRAWLER_MAX_APPLY_PER_CYCLE", testMode ? 2 : 15);
        var cycleWaitMinutes = GetOptionalIntEnv("WEBCRAWLER_CYCLE_WAIT_MINUTES", testMode ? 1 : 45);
        var usePersistentProfile = GetOptionalBoolEnv("WEBCRAWLER_USE_PERSISTENT_PROFILE", true);
        var persistIgnoredLinks = GetOptionalBoolEnv("WEBCRAWLER_PERSIST_IGNORED_LINKS", true);
        var persistSuccessfulLinks = GetOptionalBoolEnv("WEBCRAWLER_PERSIST_SUCCESSFUL_LINKS", true);
        var autoFillMandatoryFields = GetOptionalBoolEnv("WEBCRAWLER_AUTO_FILL_MANDATORY_FIELDS", true);
        var interactionDelayMinMs = GetOptionalIntEnv("WEBCRAWLER_INTERACTION_DELAY_MIN_MS", testMode ? 200 : 800);
        var interactionDelayMaxMs = GetOptionalIntEnv("WEBCRAWLER_INTERACTION_DELAY_MAX_MS", testMode ? 600 : 2500);
        var applyDelayMinMs = GetOptionalIntEnv("WEBCRAWLER_APPLY_DELAY_MIN_MS", testMode ? 800 : 5000);
        var applyDelayMaxMs = GetOptionalIntEnv("WEBCRAWLER_APPLY_DELAY_MAX_MS", testMode ? 1500 : 15000);
        var paginationDelayMinMs = GetOptionalIntEnv("WEBCRAWLER_PAGINATION_DELAY_MIN_MS", testMode ? 500 : 1200);
        var paginationDelayMaxMs = GetOptionalIntEnv("WEBCRAWLER_PAGINATION_DELAY_MAX_MS", testMode ? 1200 : 2800);
        var activeHoursStart = GetOptionalTimeOfDayEnv("WEBCRAWLER_ACTIVE_HOURS_START");
        var activeHoursEnd = GetOptionalTimeOfDayEnv("WEBCRAWLER_ACTIVE_HOURS_END");
        var autoFillFirstName = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_FIRST_NAME", "Clovis");
        var autoFillLastName = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_LAST_NAME", "Silva");
        var autoFillPhone = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_PHONE", "11999999999");
        var autoFillLocation = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_LOCATION", "Sao Paulo");
        var autoFillEmail = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_EMAIL", linkedinUsername);
        var autoFillWebsite = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_WEBSITE", string.Empty);
        var autoFillLinkedIn = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_LINKEDIN_URL", string.Empty);
        var autoFillGithub = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_GITHUB_URL", string.Empty);
        var autoFillSalary = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_SALARY", "7000");
        var autoFillGenericText = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_GENERIC_TEXT", "Tenho experiencia compativel com a vaga e disponibilidade para atuar no escopo solicitado.");
        var autoFillYearsExperience = GetOptionalIntEnv("WEBCRAWLER_DEFAULT_YEARS_EXPERIENCE", 3);
        var autoFillCheckboxTrue = GetOptionalBoolEnv("WEBCRAWLER_DEFAULT_CHECKBOX_TRUE", true);
        var autoFillWorkAuthorization = GetOptionalBoolEnv("WEBCRAWLER_DEFAULT_WORK_AUTHORIZATION", true);
        var autoFillNeedVisaSponsorship = GetOptionalBoolEnv("WEBCRAWLER_DEFAULT_NEED_VISA_SPONSORSHIP", false);
        var useJobsSearchEntry = GetOptionalBoolEnv("WEBCRAWLER_USE_JOBS_SEARCH_ENTRY", true);
        var jobsSearchTerms = GetOptionalCsvEnvList("WEBCRAWLER_JOB_SEARCH_TERMS", DefaultJobSearchTerms);

        DatabaseEnabled = !disableDatabase;
        PersistIgnoredLinksToFile = persistIgnoredLinks;
        PersistSuccessfulLinksToFile = persistSuccessfulLinks;
        AutoFillMandatoryFieldsEnabled = autoFillMandatoryFields;
        AutoFillDefaultFirstName = autoFillFirstName;
        AutoFillDefaultLastName = autoFillLastName;
        AutoFillDefaultPhone = autoFillPhone;
        AutoFillDefaultLocation = autoFillLocation;
        AutoFillDefaultEmail = autoFillEmail;
        AutoFillDefaultWebsite = autoFillWebsite;
        AutoFillDefaultLinkedIn = autoFillLinkedIn;
        AutoFillDefaultGithub = autoFillGithub;
        AutoFillDefaultSalary = autoFillSalary;
        AutoFillDefaultGenericText = autoFillGenericText;
        AutoFillDefaultYearsExperience = autoFillYearsExperience <= 0 ? 1 : autoFillYearsExperience;
        AutoFillDefaultCheckboxTrue = autoFillCheckboxTrue;
        AutoFillDefaultWorkAuthorization = autoFillWorkAuthorization;
        AutoFillDefaultNeedVisaSponsorship = autoFillNeedVisaSponsorship;
        ConfigureHumanization(
            interactionDelayMinMs,
            interactionDelayMaxMs,
            applyDelayMinMs,
            applyDelayMaxMs,
            paginationDelayMinMs,
            paginationDelayMaxMs,
            activeHoursStart,
            activeHoursEnd);

        LoadIgnoredJobLinksFromDisk();
        LoadSuccessfulJobsFromDisk();

        if (activeHoursStart.HasValue ^ activeHoursEnd.HasValue)
        {
            Console.WriteLine("Janela ativa ignorada: defina WEBCRAWLER_ACTIVE_HOURS_START e WEBCRAWLER_ACTIVE_HOURS_END em conjunto.");
        }

        Console.WriteLine($"Configuração: TEST_MODE={testMode}, DISABLE_DATABASE={disableDatabase}, MAX_PAGES_PER_CYCLE={maxPagesPerCycle}, MAX_APPLY_PER_CYCLE={maxJobsToApplyPerCycle}, CYCLE_WAIT_MINUTES={cycleWaitMinutes}, PERSIST_IGNORED_LINKS={persistIgnoredLinks}, PERSIST_SUCCESSFUL_LINKS={persistSuccessfulLinks}, AUTO_FILL_MANDATORY_FIELDS={autoFillMandatoryFields}, INTERACTION_DELAY_MS={InteractionDelayMinMs}-{InteractionDelayMaxMs}, APPLY_DELAY_MS={BetweenApplicationsDelayMinMs}-{BetweenApplicationsDelayMaxMs}, PAGINATION_DELAY_MS={PaginationDelayMinMs}-{PaginationDelayMaxMs}, ACTIVE_HOURS={DescribeActiveHoursWindow()}, USE_JOBS_SEARCH_ENTRY={useJobsSearchEntry}, JOB_SEARCH_TERMS={string.Join(" | ", jobsSearchTerms)}");

        while (true)
        {
            WaitUntilWithinActiveHoursIfNeeded();
            StartRuntimeHeartbeatLoop("running", "Iniciando novo ciclo do crawler.");
            Console.WriteLine("Iniciando nova execução...");

            if (DatabaseEnabled && !ValidateDatabaseConnection())
            {
                Console.WriteLine("Conexão com o banco indisponível. Aguardando 2 minuto(s) antes da próxima tentativa...");
                StopRuntimeHeartbeatLoop();
                SleepWithRuntimeHeartbeat(TimeSpan.FromMinutes(2), "waiting", "Banco indisponivel. Aguardando nova tentativa.");
                continue;
            }

            StartSuccessfulJobsCycle();

            using var driver = new ChromeDriver(BuildChromeOptions(usePersistentProfile));
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));

            if (!EnsureAuthenticatedSession(driver, wait, linkedinUsername, linkedinPassword))
            {
                StopRuntimeHeartbeatLoop();
                SleepWithRuntimeHeartbeat(TimeSpan.FromMinutes(1), "waiting", "Sessao nao autenticada. Aguardando antes de novo login.");
                continue;
            }

            UpdateRuntimeHeartbeatLoopState("running", "Sessao autenticada. Coletando e aplicando vagas.");

            string currentUrl = driver.Url;
            if (currentUrl.Contains("feed") || currentUrl.Contains("linkedin.com/in"))
            {
                Console.WriteLine("Login bem-sucedido! Página atual: " + currentUrl);
            }
            else
            {
                Console.WriteLine("Login falhou ou ainda está na página de login.");
            }

            var allJobsLines = new List<string>();
            var allJobsData = new List<(string Titulo, string Empresa, string Localizacao, string Link)>();
            var collectedFromJobsSearch = false;

            Console.WriteLine($"[DEBUG] useJobsSearchEntry={useJobsSearchEntry}, maxPagesPerCycle={maxPagesPerCycle}");

            if (useJobsSearchEntry)
            {
                var cycleSearchTerms = GetJobSearchTermsForCurrentCycle(jobsSearchTerms);
                Console.WriteLine($"[DEBUG] Iniciando busca por {cycleSearchTerms.Count} termos: {string.Join(" | ", cycleSearchTerms)}");
                Console.WriteLine($"Iniciando busca sequencial por competencias: {string.Join(" | ", cycleSearchTerms)}");

                foreach (var searchTerm in cycleSearchTerms)
                {
                    Console.WriteLine($"[DEBUG] Processando termo: '{searchTerm}'");
                    UpdateRuntimeHeartbeatLoopState("running", $"Coletando vagas para o termo '{searchTerm}'.");
                    Console.WriteLine($"Preparando busca de vagas para o termo '{searchTerm}'...");

                    if (!TryPrepareJobsSearchEntry(driver, searchTerm))
                    {
                        Console.WriteLine($"Falha ao preparar busca de vagas para o termo '{searchTerm}'.");
                        continue;
                    }

                    Console.WriteLine($"Entrada por busca de vagas concluida com termo: '{searchTerm}'.");

                    if (!TryCollectJobsFromCurrentResults(driver, wait, allJobsLines, allJobsData, maxPagesPerCycle, $"termo '{searchTerm}'"))
                    {
                        Console.WriteLine($"Nao foi possivel coletar vagas para o termo '{searchTerm}'.");
                        continue;
                    }

                    Console.WriteLine($"[DEBUG] Coleta concluída para '{searchTerm}'. Total acumulado: {allJobsData.Count}");
                    collectedFromJobsSearch = true;
                }

                if (!collectedFromJobsSearch)
                {
                    Console.WriteLine("Nenhuma busca por competencia foi concluida. Retornando ao fluxo classico da colecao Easy Apply.");
                }
                else
                {
                    Console.WriteLine($"[DEBUG] Coleta de todas as buscas terminada. Total: {allJobsData.Count} vagas");
                }
            }


            if (!collectedFromJobsSearch)
            {
                var collectionUrl = GetEasyApplyCollectionEntryUrlForCycle();
                Console.WriteLine($"Abrindo coleção Easy Apply... {collectionUrl}");
                driver.Navigate().GoToUrl(collectionUrl);

                if (!TryCollectJobsFromCurrentResults(driver, wait, allJobsLines, allJobsData, maxPagesPerCycle, "colecao Easy Apply"))
                {
                    Console.WriteLine("Nao foi possivel carregar a lista de vagas (timeout). Tentando novamente na proxima execucao.");
                    driver.Quit();
                    StopRuntimeHeartbeatLoop();
                    SleepWithRuntimeHeartbeat(TimeSpan.FromMinutes(2), "waiting", "Falha ao carregar a lista de vagas. Aguardando nova tentativa.");
                    continue;
                }
            }

            var totalCollectedBeforeNormalization = allJobsData.Count;
            allJobsData = NormalizeAndDeduplicateJobs(allJobsData);
            allJobsLines = allJobsData.Select(BuildJobLine).ToList();

            Console.WriteLine("\n[DEBUG] COLETANDO FINALIZANDO...");
            Console.WriteLine("\nVagas coletadas de todas as páginas:");
            Console.WriteLine($"Total de vagas coletadas: {allJobsLines.Count}");
            Console.WriteLine($"Total bruto antes de normalização/deduplicação: {totalCollectedBeforeNormalization}");

            File.WriteAllLines(JobsOutputFileName, allJobsLines);
            Console.WriteLine($"\nVagas exportadas para {JobsOutputFileName}");
            var skipApplyForCycle = maxJobsToApplyPerCycle <= 0;
            Console.WriteLine($"[DEBUG] skipApplyForCycle={skipApplyForCycle}, maxJobsToApplyPerCycle={maxJobsToApplyPerCycle}, disableDatabase={disableDatabase}");
            UpdateRuntimeHeartbeatLoopState(
                "running",
                skipApplyForCycle
                    ? "Vagas coletadas. Candidaturas desabilitadas neste ciclo."
                    : "Vagas coletadas. Aplicando candidaturas do ciclo atual.");

            if (skipApplyForCycle)
            {
                Console.WriteLine("Candidaturas automáticas desabilitadas neste ciclo porque WEBCRAWLER_MAX_APPLY_PER_CYCLE <= 0.");
            }
            else if (!disableDatabase)
            {
                Console.WriteLine("[DEBUG] Entrando em: SaveCollectedJobsToDatabase e ApplySimplifiedJobsFromDatabase");
                SaveCollectedJobsToDatabase(allJobsData);
                ApplySimplifiedJobsFromDatabase(driver, maxJobsToApplyPerCycle);
            }
            else
            {
                Console.WriteLine("[DEBUG] Entrando em: ApplySimplifiedJobsFromLinks");
                Console.WriteLine("Modo sem banco ativo. Aplicando candidaturas diretamente na lista coletada do ciclo.");
                var linksDiretos = allJobsData
                    .Select(v => v.Link)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                ApplySimplifiedJobsFromLinks(driver, linksDiretos, maxJobsToApplyPerCycle);
            }


            PrintSuccessfulJobsCycleSummary();

            Console.WriteLine("\nResumo das vagas coletadas:");
            PrintVagasTabela(allJobsData);

            Console.WriteLine("Encerrando driver...");
            driver.Quit();

            Console.WriteLine($"Execução finalizada. Aguardando {cycleWaitMinutes} minuto(s) para próxima execução...");
            StopRuntimeHeartbeatLoop();
            SleepWithRuntimeHeartbeat(TimeSpan.FromMinutes(cycleWaitMinutes), "waiting", $"Aguardando proximo ciclo por {cycleWaitMinutes} minuto(s).");
        }
    }
}
