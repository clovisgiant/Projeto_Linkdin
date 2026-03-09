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
    private static string AutoFillDefaultPhone = "11999999999";
    private static string AutoFillDefaultLocation = "Sao Paulo";
    private static string AutoFillDefaultEmail = string.Empty;
    private static string AutoFillDefaultWebsite = "";
    private static string AutoFillDefaultLinkedIn = "";
    private static string AutoFillDefaultGithub = "";
    private static string AutoFillDefaultSalary = "7000";
    private static string AutoFillDefaultGenericText = "Tenho experiencia compativel com a vaga e disponibilidade para atuar no escopo solicitado.";
    private static int AutoFillDefaultYearsExperience = 3;
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
    private const string LinkedInEasyApplyCollectionUrl = "https://www.linkedin.com/jobs/collections/easy-apply/?discover=recommended&discoveryOrigin=JOBS_HOME_JYMBII&start=0";
    private const string JobsOutputFileName = "vagas_linkedin.txt";
    private const string IgnoredJobsFileName = "ignored_job_links.txt";
    private const string SuccessfulJobsCycleFileName = "vagas_enviadas_sucesso.txt";
    private const string SuccessfulJobsHistoryFileName = "vagas_enviadas_sucesso_historico.txt";

    static void Main()
    {
        LoadEnvFileIfExists();

        var linkedinUsername = GetRequiredEnv("LINKEDIN_USERNAME");
        var linkedinPassword = GetRequiredEnv("LINKEDIN_PASSWORD");

        var testMode = GetOptionalBoolEnv("WEBCRAWLER_TEST_MODE", false);
        var disableDatabase = GetOptionalBoolEnv("WEBCRAWLER_DISABLE_DATABASE", false);
        var maxPagesPerCycle = GetOptionalIntEnv("WEBCRAWLER_MAX_PAGES_PER_CYCLE", testMode ? 1 : 0);
        var maxJobsToApplyPerCycle = GetOptionalIntEnv("WEBCRAWLER_MAX_APPLY_PER_CYCLE", testMode ? 2 : 0);
        var cycleWaitMinutes = GetOptionalIntEnv("WEBCRAWLER_CYCLE_WAIT_MINUTES", testMode ? 1 : 20);
        var usePersistentProfile = GetOptionalBoolEnv("WEBCRAWLER_USE_PERSISTENT_PROFILE", true);
        var persistIgnoredLinks = GetOptionalBoolEnv("WEBCRAWLER_PERSIST_IGNORED_LINKS", true);
        var persistSuccessfulLinks = GetOptionalBoolEnv("WEBCRAWLER_PERSIST_SUCCESSFUL_LINKS", true);
        var autoFillMandatoryFields = GetOptionalBoolEnv("WEBCRAWLER_AUTO_FILL_MANDATORY_FIELDS", true);
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

        LoadIgnoredJobLinksFromDisk();
        LoadSuccessfulJobsFromDisk();

        Console.WriteLine($"Configuração: TEST_MODE={testMode}, DISABLE_DATABASE={disableDatabase}, MAX_PAGES_PER_CYCLE={maxPagesPerCycle}, MAX_APPLY_PER_CYCLE={maxJobsToApplyPerCycle}, CYCLE_WAIT_MINUTES={cycleWaitMinutes}, PERSIST_IGNORED_LINKS={persistIgnoredLinks}, PERSIST_SUCCESSFUL_LINKS={persistSuccessfulLinks}, AUTO_FILL_MANDATORY_FIELDS={autoFillMandatoryFields}");

        while (true)
        {
            Console.WriteLine("Iniciando nova execução...");
            StartSuccessfulJobsCycle();

            using var driver = new ChromeDriver(BuildChromeOptions(usePersistentProfile));
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));

            if (!EnsureAuthenticatedSession(driver, wait, linkedinUsername, linkedinPassword))
            {
                Thread.Sleep(TimeSpan.FromMinutes(1));
                continue;
            }

            string currentUrl = driver.Url;
            if (currentUrl.Contains("feed") || currentUrl.Contains("linkedin.com/in"))
            {
                Console.WriteLine("Login bem-sucedido! Página atual: " + currentUrl);
            }
            else
            {
                Console.WriteLine("Login falhou ou ainda está na página de login.");
            }

            Console.WriteLine("Abrindo coleção Easy Apply...");
            driver.Navigate().GoToUrl(LinkedInEasyApplyCollectionUrl);

            if (!WaitForJobsResults(driver))
            {
                Console.WriteLine("Não foi possível carregar a lista de vagas (timeout). Tentando novamente na próxima execução.");
                driver.Quit();
                Thread.Sleep(TimeSpan.FromMinutes(2));
                continue;
            }

            var initialJobCards = FindJobCards(driver);
            Console.WriteLine($"Cards encontrados na página inicial: {initialJobCards.Count}");

            Console.WriteLine("Extraindo vagas da página inicial...");
            var initialExtraction = ExtractSimplifiedJobs(initialJobCards);

            foreach (var job in initialExtraction.RawLines)
            {
                Console.WriteLine(job);
            }

            var allJobsLines = new List<string>(initialExtraction.RawLines);
            var allJobsData = new List<(string Titulo, string Empresa, string Localizacao, string Link)>(initialExtraction.StructuredJobs);

            CollectJobsFromPagination(driver, wait, allJobsLines, allJobsData, maxPagesPerCycle);

            var totalCollectedBeforeNormalization = allJobsData.Count;
            allJobsData = NormalizeAndDeduplicateJobs(allJobsData);
            allJobsLines = allJobsData.Select(BuildJobLine).ToList();

            Console.WriteLine("\nVagas coletadas de todas as páginas:");
            Console.WriteLine($"Total de vagas coletadas: {allJobsLines.Count}");
            Console.WriteLine($"Total bruto antes de normalização/deduplicação: {totalCollectedBeforeNormalization}");

            File.WriteAllLines(JobsOutputFileName, allJobsLines);
            Console.WriteLine($"\nVagas exportadas para {JobsOutputFileName}");

            if (!disableDatabase)
            {
                SaveCollectedJobsToDatabase(allJobsData);
                ApplySimplifiedJobsFromDatabase(driver, maxJobsToApplyPerCycle);
            }
            else
            {
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
            Thread.Sleep(TimeSpan.FromMinutes(cycleWaitMinutes));
        }
    }
}
