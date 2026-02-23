using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Npgsql;

class Program
{
    // String de conexão lida de variável de ambiente para não expor credenciais em código.
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("WEBCRAWLER_DB_CONNECTION")
        ?? throw new InvalidOperationException("Defina a variável de ambiente WEBCRAWLER_DB_CONNECTION.");

    // Lê variável de ambiente obrigatória e lança erro amigável quando ausente.
    private static string GetRequiredEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Defina a variável de ambiente {name}.");
        }

        return value;
    }

    // Garante estrutura de rastreabilidade da automação (etapas, status e evidências).
    private static void EnsureApplicationTrackingSchema(NpgsqlConnection conn)
    {
        using var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS candidatura_etapas (
                id BIGSERIAL PRIMARY KEY,
                link TEXT NOT NULL,
                etapa TEXT NOT NULL,
                sucesso BOOLEAN NOT NULL,
                detalhe TEXT NULL,
                html_path TEXT NULL,
                screenshot_path TEXT NULL,
                criado_em TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_candidatura_etapas_link_criado_em
            ON candidatura_etapas (link, criado_em DESC);
        ", conn);
        cmd.ExecuteNonQuery();
    }

    // Registra no banco cada etapa do fluxo de candidatura para auditoria/depuração.
    private static void LogApplicationStep(
        string link,
        string etapa,
        bool sucesso,
        string? detalhe = null,
        string? htmlPath = null,
        string? screenshotPath = null)
    {
        if (string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(etapa))
        {
            return;
        }

        try
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();
            EnsureApplicationTrackingSchema(conn);

            using var cmd = new NpgsqlCommand(
                @"INSERT INTO candidatura_etapas (link, etapa, sucesso, detalhe, html_path, screenshot_path)
                  VALUES (@link, @etapa, @sucesso, @detalhe, @html_path, @screenshot_path)",
                conn);

            cmd.Parameters.AddWithValue("link", link);
            cmd.Parameters.AddWithValue("etapa", etapa);
            cmd.Parameters.AddWithValue("sucesso", sucesso);
            cmd.Parameters.AddWithValue("detalhe", (object?)detalhe ?? DBNull.Value);
            cmd.Parameters.AddWithValue("html_path", (object?)htmlPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("screenshot_path", (object?)screenshotPath ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Falha ao registrar etapa '{etapa}' no banco: {ex.Message}");
        }
    }

    static void Main()
    {
        var linkedinUsername = GetRequiredEnv("LINKEDIN_USERNAME");
        var linkedinPassword = GetRequiredEnv("LINKEDIN_PASSWORD");

        // Loop infinito para manter o robô executando de forma recorrente.
        while (true)
        {
            // Log de início de ciclo para facilitar acompanhamento no terminal.
            Console.WriteLine("Iniciando nova execução...");

            // Define opções de inicialização do Chrome usado pelo Selenium.
            var options = new ChromeOptions();

            // Se quiser executar sem abrir janela, descomente a linha abaixo.
            // options.AddArgument("--headless"); // Executa sem abrir janela

            // Evita uso de aceleração por GPU (mais estável em alguns ambientes).
            options.AddArgument("--disable-gpu");

            // Reduz problemas de memória compartilhada em algumas máquinas/containers.
            options.AddArgument("--disable-dev-shm-usage");

            // Cria o driver do navegador (descartado automaticamente ao fim do ciclo).
            using var driver = new ChromeDriver(options);

            // Wait padrão para elementos e condições de navegação na primeira parte do fluxo.
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));

            // Log de navegação para página principal do LinkedIn.
            Console.WriteLine("Abrindo LinkedIn...");
            driver.Navigate().GoToUrl("https://www.linkedin.com/");

            // Captura título para confirmar que a página respondeu corretamente.
            var pageTitle = driver.Title;
            Console.WriteLine("Título da página: " + pageTitle);

            // Vai para a tela de login.
            Console.WriteLine("Abrindo tela de login...");
            driver.Navigate().GoToUrl("https://www.linkedin.com/login");

            // Informa no log que vai preencher credenciais.
            Console.WriteLine("Preenchendo credenciais...");

            // Aguarda o campo de usuário estar disponível.
            var emailField = wait.Until(d => d.FindElement(By.Id("username")));

            // Limpa qualquer valor pré-preenchido no campo de e-mail.
            emailField.Clear();

            // Digita o e-mail de login.
            emailField.SendKeys(linkedinUsername);

            // Busca campo de senha.
            var passwordField = driver.FindElement(By.Id("password"));

            // Limpa o campo de senha antes de escrever.
            passwordField.Clear();

            // Digita a senha de login.
            passwordField.SendKeys(linkedinPassword);

            // Localiza o botão de submit do formulário de login.
            var loginButton = driver.FindElement(By.XPath("//button[@type='submit']"));

            // Dispara o clique no botão para autenticar.
            loginButton.Click();
            Console.WriteLine("Login enviado, aguardando redirecionamento...");

            // Aguarda mudança de URL para páginas comuns após login.
            wait.Until(d => d.Url.Contains("feed") || d.Url.Contains("linkedin.com/in"));

            // Guarda URL atual para validação simples do sucesso do login.
            string currentUrl = driver.Url;
            if (currentUrl.Contains("feed") || currentUrl.Contains("linkedin.com/in"))
            {
                // Login concluído com redirecionamento esperado.
                Console.WriteLine("Login bem-sucedido! Página atual: " + currentUrl);
            }
            else
            {
                // Fallback de log caso redirecionamento esperado não ocorra.
                Console.WriteLine("Login falhou ou ainda está na página de login.");
            }

            // Acessa coleção de vagas com candidatura simplificada.
            Console.WriteLine("Abrindo coleção Easy Apply...");
            driver.Navigate().GoToUrl("https://www.linkedin.com/jobs/collections/easy-apply/?discover=recommended&discoveryOrigin=JOBS_HOME_JYMBII&start=0");

            // Se não carregar a lista de vagas no tempo esperado, encerra ciclo atual e tenta depois.
            if (!WaitForJobsResults(driver))
            {
                Console.WriteLine("Não foi possível carregar a lista de vagas (timeout). Tentando novamente na próxima execução.");
                driver.Quit();
                Thread.Sleep(TimeSpan.FromMinutes(2));
                continue;
            }

            // Captura os cards de vagas da página inicial.
            var jobCardsExibirTudo = FindJobCards(driver);
            Console.WriteLine($"Cards encontrados na página inicial: {jobCardsExibirTudo.Count}");

            // Lista textual para exportação em TXT.
            var jobsExibirTudo = new List<string>();

            // Lista tipada para gravação no banco.
            var vagasObj = new List<(string Titulo, string Empresa, string Localizacao, string Link)>();

            // Inicia extração de dados dos cards da página atual.
            Console.WriteLine("Extraindo vagas da página inicial...");
            foreach (var card in jobCardsExibirTudo)
            {
                try
                {
                    // Filtra apenas vagas com candidatura simplificada.
                    if (!HasSimplifiedApplication(card))
                    {
                        continue;
                    }

                    // Extrai título testando múltiplos seletores (fallback para mudanças de layout).
                    var title = GetTextBySelectors(card,
                        "a.job-card-container__link strong",
                        "a.job-card-list__title",
                        "a.job-card-container__link span[aria-hidden='true']");

                    // Extrai empresa com fallback de seletores.
                    var company = GetTextBySelectors(card,
                        ".artdeco-entity-lockup__subtitle",
                        "a.job-card-container__company-name",
                        ".job-card-container__primary-description");

                    // Extrai localização com fallback de seletores.
                    var location = GetTextBySelectors(card,
                        ".job-card-container__metadata-wrapper span",
                        "span.job-card-container__metadata-item",
                        ".job-card-container__metadata-item");

                    // Extrai URL da vaga.
                    var link = GetAttributeBySelectors(card, "href",
                        "a.job-card-container__link",
                        "a.job-card-list__title");

                    // Monta linha textual usada em log e exportação para arquivo.
                    var vagaLinha = $"{title} | {company} | {location} | {link}";
                    Console.WriteLine($"Vaga encontrada: {vagaLinha}");

                    // Salva linha na lista de exportação TXT.
                    jobsExibirTudo.Add(vagaLinha);

                    // Salva estrutura tipada para persistência no banco.
                    vagasObj.Add((title ?? "", company ?? "", location ?? "", link ?? ""));
                }
                catch (NoSuchElementException)
                {
                    // Se o card mudou no meio da leitura, ignora o item e segue.
                    continue;
                }
                catch (Exception ex)
                {
                    // Log de erro inesperado para diagnóstico de manutenção.
                    Console.WriteLine($"Erro inesperado: {ex.Message}");
                    continue;
                }
            }

            // Mostra no console as linhas extraídas da primeira página.
            foreach (var job in jobsExibirTudo)
            {
                Console.WriteLine(job);
            }

            // Inicializa listas finais com os dados já coletados na primeira página.
            var todasVagas = new List<string>(jobsExibirTudo);
            var todasVagasObj = new List<(string Titulo, string Empresa, string Localizacao, string Link)>(vagasObj);

            // Flag de controle para continuar enquanto houver botão de próxima página.
            bool temMaisPaginas = true;
            Console.WriteLine("Iniciando paginação...");

            // Percorre páginas de resultado até não haver próxima página.
            while (temMaisPaginas)
            {
                // Busca botões de paginação visíveis.
                var paginacaoBotoes = driver.FindElements(By.CssSelector("ul.jobs-search-pagination__pages button.jobs-search-pagination__indicator-button"));

                // Índice da página atual (inicialmente inválido).
                int paginaAtual = -1;

                // Descobre qual botão representa a página ativa.
                for (int i = 0; i < paginacaoBotoes.Count; i++)
                {
                    if (paginacaoBotoes[i].GetAttribute("aria-current") == "page")
                    {
                        paginaAtual = i;
                        break;
                    }
                }

                // Se existir próxima página, navega para ela.
                if (paginaAtual >= 0 && paginaAtual + 1 < paginacaoBotoes.Count)
                {
                    // Obtém botão da próxima página.
                    var nextButton = paginacaoBotoes[paginaAtual + 1];

                    // Aguarda botão estar de fato interagível; se não ficar pronto, encerra paginação sem abortar execução.
                    bool nextButtonReady;
                    try
                    {
                        nextButtonReady = wait.Until(d => nextButton.Displayed && nextButton.Enabled);
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

                    // Clica para trocar a página.
                    nextButton.Click();

                    // Pequena espera para renderização da nova lista de vagas.
                    Thread.Sleep(2500);

                    // Coleta cards da página recém-carregada.
                    var jobCardsPagina = FindJobCards(driver);
                    Console.WriteLine($"Cards encontrados na página: {jobCardsPagina.Count}");

                    // Extrai dados de cada card da página atual.
                    foreach (var card in jobCardsPagina)
                    {
                        try
                        {
                            // Filtra apenas vagas com candidatura simplificada.
                            if (!HasSimplifiedApplication(card))
                            {
                                continue;
                            }

                            // Extrai título da vaga.
                            var title = GetTextBySelectors(card,
                                "a.job-card-container__link strong",
                                "a.job-card-list__title",
                                "a.job-card-container__link span[aria-hidden='true']");

                            // Extrai empresa da vaga.
                            var company = GetTextBySelectors(card,
                                ".artdeco-entity-lockup__subtitle",
                                "a.job-card-container__company-name",
                                ".job-card-container__primary-description");

                            // Extrai localização da vaga.
                            var location = GetTextBySelectors(card,
                                ".job-card-container__metadata-wrapper span",
                                "span.job-card-container__metadata-item",
                                ".job-card-container__metadata-item");

                            // Extrai link da vaga.
                            var link = GetAttributeBySelectors(card, "href",
                                "a.job-card-container__link",
                                "a.job-card-list__title");

                            // Formata linha consolidada da vaga.
                            var vagaLinha = $"{title} | {company} | {location} | {link}";
                            Console.WriteLine($"Vaga encontrada: {vagaLinha}");

                            // Acumula para exportação em arquivo.
                            todasVagas.Add(vagaLinha);

                            // Acumula para persistência em banco.
                            todasVagasObj.Add((title ?? "", company ?? "", location ?? "", link ?? ""));
                        }
                        catch (NoSuchElementException)
                        {
                            // Ignora cards que perderam elementos durante renderização.
                            continue;
                        }
                        catch (Exception ex)
                        {
                            // Registra erro para manutenção futura e continua varredura.
                            Console.WriteLine($"Erro inesperado: {ex.Message}");
                            continue;
                        }
                    }
                }
                else
                {
                    // Não há próxima página; encerra loop de paginação.
                    temMaisPaginas = false;
                }
            }

            // Mostra estatística final da coleta.
            Console.WriteLine("\nVagas coletadas de todas as páginas:");
            Console.WriteLine($"Total de vagas coletadas: {todasVagas.Count}");

            // Define caminho do arquivo de saída.
            string caminhoArquivo = "vagas_linkedin.txt";

            // Escreve todas as vagas no arquivo TXT.
            System.IO.File.WriteAllLines(caminhoArquivo, todasVagas);
            Console.WriteLine($"\nVagas exportadas para {caminhoArquivo}");

            // Tenta persistir dados no PostgreSQL.
            try
            {
                Console.WriteLine("Salvando no PostgreSQL...");

                // Abre conexão com banco de dados.
                using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
                conn.Open();

                // Garante existência da coluna de candidatura simplificada.
                using (var ensureColumn = new Npgsql.NpgsqlCommand(
                    "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_simplificada BOOLEAN;", conn))
                {
                    ensureColumn.ExecuteNonQuery();
                }

                // Garante existência da coluna de controle para evitar reaplicação da mesma vaga.
                using (var ensureAppliedColumn = new Npgsql.NpgsqlCommand(
                    "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_enviada BOOLEAN DEFAULT FALSE;", conn))
                {
                    ensureAppliedColumn.ExecuteNonQuery();
                }

                // Garante coluna com data/hora da candidatura efetivamente enviada.
                using (var ensureAppliedAtColumn = new Npgsql.NpgsqlCommand(
                    "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_candidatura TIMESTAMP NULL;", conn))
                {
                    ensureAppliedAtColumn.ExecuteNonQuery();
                }

                // Garante tabela de rastreabilidade por etapa da candidatura.
                EnsureApplicationTrackingSchema(conn);

                // Insere cada vaga no banco evitando duplicidade por link.
                foreach (var vaga in todasVagasObj)
                {
                    using var cmd = new Npgsql.NpgsqlCommand(
                        "INSERT INTO vagas (titulo, empresa, localizacao, link, data_insercao, candidatura_simplificada, candidatura_enviada) VALUES (@titulo, @empresa, @localizacao, @link, @data_insercao, @candidatura_simplificada, @candidatura_enviada) ON CONFLICT (link) DO NOTHING", conn);
                    cmd.Parameters.AddWithValue("titulo", vaga.Titulo);
                    cmd.Parameters.AddWithValue("empresa", vaga.Empresa);
                    cmd.Parameters.AddWithValue("localizacao", vaga.Localizacao);
                    cmd.Parameters.AddWithValue("link", vaga.Link);
                    cmd.Parameters.AddWithValue("data_insercao", DateTime.Now);
                    cmd.Parameters.AddWithValue("candidatura_simplificada", true);
                    cmd.Parameters.AddWithValue("candidatura_enviada", false);
                    cmd.ExecuteNonQuery();
                }

                // Log de sucesso de persistência.
                Console.WriteLine("Vagas salvas no banco PostgreSQL (sem duplicidade)!");
            }
            catch (Exception ex)
            {
                // Log de erro de banco para manutenção.
                Console.WriteLine($"Erro ao salvar no banco: {ex.Message}");
            }

            // Depois de salvar/atualizar o banco, abre as vagas salvas e clica em candidatura simplificada.
            ApplySimplifiedJobsFromDatabase(driver);

            // Exibe tabela resumida no console.
            Console.WriteLine("\nResumo das vagas coletadas:");
            PrintVagasTabela(todasVagasObj);

            // Encerra o navegador explicitamente.
            Console.WriteLine("Encerrando driver...");
            driver.Quit();

            // Aguarda antes de iniciar o próximo ciclo de varredura.
            Console.WriteLine("Execução finalizada. Aguardando 20 minutos para próxima execução...");
            Thread.Sleep(TimeSpan.FromMinutes(20));
        }
    }

    // Busca no banco os links de vagas com candidatura simplificada que ainda não foram enviadas.
    private static List<string> GetSimplifiedJobLinksFromDatabase()
    {
        // Lista final de URLs retornadas pelo SELECT.
        var links = new List<string>();

        // Abre conexão com PostgreSQL.
        using var conn = new NpgsqlConnection(ConnectionString);
        conn.Open();

        // Garante que a coluna de controle exista antes do SELECT.
        using (var ensureAppliedColumn = new NpgsqlCommand(
            "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_enviada BOOLEAN DEFAULT FALSE;",
            conn))
        {
            ensureAppliedColumn.ExecuteNonQuery();
        }

        // Garante existência da coluna de data de candidatura.
        using (var ensureAppliedAtColumn = new NpgsqlCommand(
            "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_candidatura TIMESTAMP NULL;",
            conn))
        {
            ensureAppliedAtColumn.ExecuteNonQuery();
        }

        // Garante tabela de rastreabilidade por etapa da candidatura.
        EnsureApplicationTrackingSchema(conn);

        // Seleciona apenas links válidos de vagas simplificadas ainda não enviadas.
        using var cmd = new NpgsqlCommand(
            "SELECT DISTINCT link FROM vagas WHERE candidatura_simplificada = TRUE AND COALESCE(candidatura_enviada, FALSE) = FALSE AND link IS NOT NULL AND link <> ''",
            conn);

        // Executa leitura linha a linha do resultado.
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            // Adiciona o campo link (coluna 0) na lista.
            links.Add(reader.GetString(0));
        }

        // Devolve todos os links encontrados.
        return links;
    }

    // Marca no banco que a candidatura para o link foi enviada (clique realizado com sucesso).
    private static void MarkJobAsApplied(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return;
        }

        // Abre conexão para atualizar status da vaga.
        using var conn = new NpgsqlConnection(ConnectionString);
        conn.Open();

        // Atualiza flag e data da candidatura enviada para não repetir em próximos ciclos.
        using var cmd = new NpgsqlCommand(
            "UPDATE vagas SET candidatura_enviada = TRUE, data_candidatura = @data_candidatura WHERE link = @link",
            conn);
        cmd.Parameters.AddWithValue("data_candidatura", DateTime.Now);
        cmd.Parameters.AddWithValue("link", link);
        cmd.ExecuteNonQuery();
    }

    // Faz clique robusto com tentativa normal e fallback por JavaScript.
    private static void ClickElementRobust(IWebDriver driver, IWebElement element)
    {
        try
        {
            element.Click();
        }
        catch
        {
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", element);
        }
    }

    // Aguarda carregamento base da página de vaga.
    private static void WaitForJobPageReady(IWebDriver driver)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(25));
        wait.Until(d =>
        {
            try
            {
                var state = ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString();
                return state == "complete";
            }
            catch
            {
                return false;
            }
        });

        // Tempo curto para estabilizar elementos dinâmicos do LinkedIn.
        Thread.Sleep(1200);
    }

    // Sanitiza nomes para uso seguro em arquivos de diagnóstico.
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

    // Extrai um identificador da vaga a partir do link para facilitar rastreio de arquivos.
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

    // Salva diagnóstico (HTML + screenshot) para investigar por que o botão não foi encontrado.
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

    // Tenta selecionar um currículo no popup de candidatura (quando a etapa existir).
    private static void TrySelectResumeInPopup(IWebDriver driver)
    {
        try
        {
            // Se já existir um currículo selecionado, mantém como está.
            var alreadySelected = driver.FindElements(By.CssSelector("input[type='radio'][name*='resume']:checked, input[type='radio'][id*='resume']:checked"));
            if (alreadySelected.Count > 0)
            {
                Console.WriteLine("Currículo já selecionado no popup.");
                return;
            }

            // Tenta localizar opções de currículo por seletores comuns da tela Easy Apply.
            var resumeCandidates = driver.FindElements(By.CssSelector(
                "label[for*='resume'], input[type='radio'][name*='resume'], [data-test-document-upload-item], div.jobs-document-upload-redesign-card__container"));

            if (resumeCandidates.Count == 0)
            {
                Console.WriteLine("Nenhuma opção de currículo encontrada nesta etapa (pode não ser necessária). ");
                return;
            }

            // Seleciona a primeira opção disponível.
            var resumeOption = resumeCandidates[0];
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", resumeOption);
            Thread.Sleep(300);
            ClickElementRobust(driver, resumeOption);
            Console.WriteLine("Currículo selecionado no popup.");
            Thread.Sleep(500);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Não foi possível selecionar currículo automaticamente: {ex.Message}");
        }
    }

    // Localiza e clica no botão "Avançar" da etapa atual do popup.
    private static bool TryClickNextButtonInPopup(IWebDriver driver, WebDriverWait wait, string stepName, string link)
    {
        IWebElement? nextButton = null;

                // Prioriza o botão exato informado: data-easy-apply-next-button + data-live-test-easy-apply-next-button.
                try
                {
                        var js = (IJavaScriptExecutor)driver;
                        for (int attempt = 0; attempt < 12; attempt++)
                        {
                                var clicked = js.ExecuteScript(@"
                                        const selectors = [
                                            'button[data-easy-apply-next-button][data-live-test-easy-apply-next-button]',
                                            'button[data-live-test-easy-apply-next-button]',
                                            'button[data-easy-apply-next-button]',
                                            'button[aria-label=""Avançar para próxima etapa""]'
                                        ];

                                        for (const selector of selectors) {
                                            const candidates = Array.from(document.querySelectorAll(selector));
                                            for (const candidate of candidates) {
                                                const style = window.getComputedStyle(candidate);
                                                const visible = style && style.display !== 'none' && style.visibility !== 'hidden' && candidate.offsetParent !== null;
                                                const enabled = !candidate.disabled && candidate.getAttribute('aria-disabled') !== 'true';

                                                if (visible && enabled) {
                                                    candidate.scrollIntoView({ block: 'center' });
                                                    candidate.click();
                                                    return true;
                                                }
                                            }
                                        }

                                        return false;
                                ");

                                if (clicked is bool ok && ok)
                                {
                                        Console.WriteLine($"Botão 'Avançar' (seletor exato) clicado com sucesso na etapa: {stepName}.");
                                        LogApplicationStep(link, $"next_clicked_exact_{stepName}", true, "Botão exato de Avançar clicado por seletor data-easy-apply-next-button.");
                                        return true;
                                }

                                Thread.Sleep(300);
                        }
                }
                catch
                {
                        // Se falhar a estratégia exata por JS, segue para os fallbacks abaixo.
                }

        try
        {
            nextButton = wait.Until(d =>
            {
                var possibleRoots = d.FindElements(By.CssSelector(
                    "div.jobs-easy-apply-modal, " +
                    "div.jobs-easy-apply-modal-content, " +
                    "div[role='dialog']"
                ));

                foreach (var root in possibleRoots)
                {
                    try
                    {
                        if (!root.Displayed)
                        {
                            continue;
                        }

                        var exactCandidates = root.FindElements(By.CssSelector(
                            "button[data-easy-apply-next-button], " +
                            "button[data-live-test-easy-apply-next-button], " +
                            "button[aria-label='Avançar para próxima etapa']"
                        ));

                        foreach (var candidate in exactCandidates)
                        {
                            try
                            {
                                if (candidate.Displayed && candidate.Enabled)
                                {
                                    return candidate;
                                }
                            }
                            catch
                            {
                                // Tenta próximo candidato.
                            }
                        }

                        var candidates = root.FindElements(By.CssSelector(
                            "button[aria-label*='Avançar'], " +
                            "button[aria-label*='Next'], " +
                            "button[aria-label*='Continuar'], " +
                            "button[aria-label*='Continue'], " +
                            "button.artdeco-button--primary"
                        ));

                        foreach (var candidate in candidates)
                        {
                            try
                            {
                                var text = candidate.Text?.Trim() ?? string.Empty;
                                var ariaLabel = candidate.GetAttribute("aria-label")?.Trim() ?? string.Empty;
                                var combined = (text + " " + ariaLabel).ToLowerInvariant();

                                if (!candidate.Displayed || !candidate.Enabled)
                                {
                                    continue;
                                }

                                if (combined.Contains("avançar") ||
                                    combined.Contains("next") ||
                                    combined.Contains("continuar") ||
                                    combined.Contains("continue") ||
                                    combined.Contains("prosseguir"))
                                {
                                    return candidate;
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
                        // Tenta próximo root.
                    }
                }

                return null;
            });
        }
        catch (WebDriverTimeoutException)
        {
            // Fallback por texto/aria-label do botão Avançar/Next/Continuar (PT/EN).
            var nextCandidates = driver.FindElements(By.XPath(
                "//div[contains(@class,'jobs-easy-apply-modal') or @role='dialog']" +
                "//button[contains(@aria-label,'Avançar') or contains(@aria-label,'Next') or contains(@aria-label,'Continuar') or contains(@aria-label,'Continue') or " +
                ".//span[contains(normalize-space(.),'Avançar')] or .//span[contains(normalize-space(.),'Next')] or " +
                ".//span[contains(normalize-space(.),'Continuar')] or .//span[contains(normalize-space(.),'Continue')] or " +
                ".//span[contains(normalize-space(.),'Prosseguir')]]"));

            if (nextCandidates.Count > 0)
            {
                nextButton = nextCandidates[0];
            }
        }

        if (nextButton == null)
        {
            Console.WriteLine($"Botão 'Avançar' não encontrado na etapa: {stepName}.");
            var diagnostics = SaveFailureDiagnostics(driver, link, $"next_not_found_{stepName}");
            LogApplicationStep(link, $"next_not_found_{stepName}", false, "Botão Avançar não encontrado.", diagnostics.HtmlPath, diagnostics.ScreenshotPath);
            return false;
        }

        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", nextButton);
        Thread.Sleep(300);
        ClickElementRobust(driver, nextButton);
        Console.WriteLine($"Botão 'Avançar' clicado com sucesso na etapa: {stepName}.");
        LogApplicationStep(link, $"next_clicked_{stepName}", true, "Botão Avançar clicado com sucesso.");
        return true;
    }

    // Clica no botão "Revisar" quando essa etapa existir no popup.
    private static bool TryClickReviewButtonInPopup(IWebDriver driver, WebDriverWait wait, string link)
    {
        IWebElement? reviewButton = null;
        try
        {
            reviewButton = wait.Until(d => d.FindElement(By.CssSelector("button[data-live-test-easy-apply-review-button]")));
        }
        catch (WebDriverTimeoutException)
        {
            // Fallback por aria-label/texto em PT/EN.
            var reviewCandidates = driver.FindElements(By.XPath("//button[contains(@aria-label,'Revise sua candidatura') or contains(@aria-label,'Review your application') or .//span[contains(normalize-space(.),'Revisar')] or .//span[contains(normalize-space(.),'Review')]]"));
            if (reviewCandidates.Count > 0)
            {
                reviewButton = reviewCandidates[0];
            }
        }

        if (reviewButton == null)
        {
            Console.WriteLine("Etapa 'Revisar' não apareceu nesta vaga; seguindo fluxo.");
            LogApplicationStep(link, "review_not_present", true, "Etapa Revisar não exigida para esta vaga.");
            return false;
        }

        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", reviewButton);
        Thread.Sleep(300);
        ClickElementRobust(driver, reviewButton);
        Console.WriteLine("Botão 'Revisar' clicado com sucesso.");
        LogApplicationStep(link, "review_clicked", true, "Botão Revisar clicado com sucesso.");
        return true;
    }

    // Em alguns fluxos SDUI não há botão "Avançar"; tenta finalizar por Revisar/Enviar diretamente.
    private static bool TryFinalizeWithoutNextIfPossible(IWebDriver driver, WebDriverWait wait, string link)
    {
        Console.WriteLine("[FALLBACK] Tentando finalizar sem etapa 'Avançar'...");

        // Primeiro tenta ir para revisão (quando houver).
        TryClickReviewButtonInPopup(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(4)), link);
        Thread.Sleep(600);

        IWebElement? submitButton = null;
        try
        {
            submitButton = wait.Until(d => d.FindElement(By.CssSelector("button[data-live-test-easy-apply-submit-button]")));
        }
        catch (WebDriverTimeoutException)
        {
            var submitCandidates = driver.FindElements(By.XPath(
                "//button[contains(@aria-label,'Enviar candidatura') or contains(@aria-label,'Submit application') or " +
                ".//span[contains(normalize-space(.),'Enviar candidatura')] or .//span[contains(normalize-space(.),'Submit application')]]"));

            if (submitCandidates.Count > 0)
            {
                submitButton = submitCandidates[0];
            }
        }

        if (submitButton == null)
        {
            LogApplicationStep(link, "fallback_finalize_not_possible", false, "Sem botão Avançar e sem botão de envio direto.");
            return false;
        }

        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", submitButton);
        Thread.Sleep(250);
        ClickElementRobust(driver, submitButton);
        Console.WriteLine("[FALLBACK] Botão 'Enviar candidatura' clicado sem etapa 'Avançar'.");
        LogApplicationStep(link, "fallback_submit_clicked", true, "Envio direto realizado sem etapa Avançar.");
        return true;
    }

    // Em alguns layouts, após Easy Apply surge um botão/link "Continuar" antes do primeiro "Avançar".
    private static bool TryClickContinueEntryIfPresent(IWebDriver driver, WebDriverWait wait, string link)
    {
        IWebElement? continueButton = null;

        try
        {
            continueButton = wait.Until(d =>
            {
                var candidates = d.FindElements(By.XPath(
                    "//a[contains(@href,'/apply/?openSDUIApplyFlow=true')] | " +
                    "//button[contains(@aria-label,'Continuar') or contains(@aria-label,'Continue') or contains(@aria-label,'Prosseguir')] | " +
                    "//a[contains(@aria-label,'Continuar') or contains(@aria-label,'Continue') or contains(@aria-label,'Prosseguir')] | " +
                    "//button[.//span[contains(normalize-space(.),'Continuar') or contains(normalize-space(.),'Continue') or contains(normalize-space(.),'Prosseguir')]] | " +
                    "//a[.//span[contains(normalize-space(.),'Continuar') or contains(normalize-space(.),'Continue') or contains(normalize-space(.),'Prosseguir')]]"));

                foreach (var candidate in candidates)
                {
                    try
                    {
                        if (candidate.Displayed && candidate.Enabled)
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                        // Tenta próximo candidato.
                    }
                }

                return null;
            });
        }
        catch (WebDriverTimeoutException)
        {
            // Não existe etapa de "Continuar" para esta vaga.
        }

        if (continueButton == null)
        {
            return false;
        }

        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", continueButton);
        Thread.Sleep(250);
        ClickElementRobust(driver, continueButton);
        Console.WriteLine("Botão/link 'Continuar' clicado antes da etapa 'Avançar'.");
        LogApplicationStep(link, "continue_entry_clicked", true, "Botão/link Continuar clicado para abrir fluxo de candidatura.");
        Thread.Sleep(800);
        return true;
    }

    // Executa fluxo completo sem perguntas obrigatórias: Candidatura simplificada -> Avançar -> selecionar currículo -> Avançar -> Enviar candidatura.
    private static bool TryCompleteEasyApplyWithoutQuestions(IWebDriver driver, WebDriverWait wait, string link)
    {
        Console.WriteLine("[ETAPA] Procurando botão de candidatura simplificada...");
        LogApplicationStep(link, "flow_started", true, "Início do fluxo Easy Apply.");

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
        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", applyButton);
        Thread.Sleep(500);

        // Clica no botão de candidatura simplificada.
        ClickElementRobust(driver, applyButton);
        Console.WriteLine("Botão de candidatura simplificada clicado com sucesso.");
        LogApplicationStep(link, "easy_apply_clicked", true, "Botão inicial Easy Apply clicado.");

        // Em vagas SDUI, pode existir um botão/link intermediário "Continuar" antes do popup padrão.
        TryClickContinueEntryIfPresent(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(5)), link);

        // Primeiro avanço no popup após abrir candidatura simplificada.
        Console.WriteLine("[ETAPA] Tentando primeiro 'Avançar'...");
        if (!TryClickNextButtonInPopup(driver, wait, "primeira etapa", link))
        {
            if (!TryFinalizeWithoutNextIfPossible(driver, wait, link))
            {
                return false;
            }

            MarkJobAsApplied(link);
            LogApplicationStep(link, "application_completed_via_fallback", true, "Fluxo concluído por fallback sem etapa Avançar.");
            return true;
        }

        // Aguarda curta para transição de etapa do popup.
        Thread.Sleep(800);

        // Seleciona currículo na etapa seguinte quando necessário.
        Console.WriteLine("[ETAPA] Tentando selecionar currículo...");
        TrySelectResumeInPopup(driver);
        LogApplicationStep(link, "resume_step_processed", true, "Etapa de currículo processada.");

        // Segundo avanço no popup após seleção de currículo.
        Console.WriteLine("[ETAPA] Tentando segundo 'Avançar'...");
        if (!TryClickNextButtonInPopup(driver, wait, "etapa de currículo", link))
        {
            return false;
        }

        // Algumas vagas exibem etapa de revisão antes do envio.
        Console.WriteLine("[ETAPA] Tentando etapa 'Revisar' (quando existir)...");
        var reviewed = TryClickReviewButtonInPopup(driver, wait, link);
        if (reviewed)
        {
            // Aguarda transição para a tela de envio após revisão.
            Thread.Sleep(800);
        }

        // Aguarda transição para a etapa final de envio.
        Thread.Sleep(800);

        // Tenta localizar botão final de envio da candidatura.
        IWebElement? submitButton = null;
        try
        {
            submitButton = wait.Until(d => d.FindElement(By.CssSelector("button[data-live-test-easy-apply-submit-button]")));
        }
        catch (WebDriverTimeoutException)
        {
            // Fallback por aria-label/texto do botão de envio.
            var submitCandidates = driver.FindElements(By.XPath("//button[contains(@aria-label,'Enviar candidatura') or .//span[contains(normalize-space(.),'Enviar candidatura')]]"));
            if (submitCandidates.Count > 0)
            {
                submitButton = submitCandidates[0];
            }
        }

        // Se não encontrou o botão de envio, provavelmente há perguntas/etapas obrigatórias.
        if (submitButton == null)
        {
            Console.WriteLine("Vaga com etapas obrigatórias adicionais detectada. Ignorando por enquanto.");
            var diagnostics = SaveFailureDiagnostics(driver, link, "submit_not_found_or_required_questions");
            LogApplicationStep(link, "submit_not_found_or_required_questions", false, "Fluxo exige etapas adicionais/perguntas obrigatórias.", diagnostics.HtmlPath, diagnostics.ScreenshotPath);
            return false;
        }

        // Scroll e clique no botão final para enviar candidatura.
        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", submitButton);
        Thread.Sleep(300);
        ClickElementRobust(driver, submitButton);
        Console.WriteLine("Botão 'Enviar candidatura' clicado com sucesso.");
        LogApplicationStep(link, "submit_clicked", true, "Botão Enviar candidatura clicado com sucesso.");

        return true;
    }

    // Abre cada link vindo do banco e executa o fluxo de candidatura simplificada.
    private static void ApplySimplifiedJobsFromDatabase(IWebDriver driver)
    {
        try
        {
            // Busca links de vagas simplificadas já persistidas no banco.
            var links = GetSimplifiedJobLinksFromDatabase();
            Console.WriteLine($"Total de links com candidatura simplificada no banco: {links.Count}");

            // Wait usado para localizar o botão de candidatura em cada página.
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(25));

            // Percorre cada URL retornada no SELECT.
            foreach (var link in links)
            {
                if (string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

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
                        continue;
                    }

                    // Marca no banco somente quando o fluxo do popup foi concluído.
                    MarkJobAsApplied(link);
                    Console.WriteLine("Vaga marcada como candidatura_enviada = TRUE no banco.");
                    LogApplicationStep(link, "job_marked_as_applied", true, "Vaga marcada como candidatura enviada.");

                    // Espera curta para estabilização de modal/fluxo antes da próxima vaga.
                    Thread.Sleep(1500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao processar vaga {link}: {ex.Message}");
                    var diagnostics = SaveFailureDiagnostics(driver, link, "job_processing_exception");
                    LogApplicationStep(link, "job_processing_exception", false, ex.Message, diagnostics.HtmlPath, diagnostics.ScreenshotPath);
                }
            }
        }
        catch (Exception ex)
        {
            // Loga erro geral da etapa de candidatura automática por links do banco.
            Console.WriteLine($"Erro na rotina de candidatura por banco: {ex.Message}");
        }
    }

    // Retorna lista de cards de vagas usando múltiplos seletores para maior robustez.
    private static IReadOnlyCollection<IWebElement> FindJobCards(IWebDriver driver)
    {
        // Busca os cards com seletores alternativos para cenários de mudança de HTML.
        var cards = driver.FindElements(By.CssSelector("ul.jobs-search-results__list > li, ul.scaffold-layout__list-container > li, li.jobs-search-results__list-item, ul.jobs-search__results-list > li, ul.scaffold-layout__list-container li, div.job-card-container, a.job-card-container__link"));

        // Entrega a coleção para o chamador.
        return cards;
    }

    // Aguarda a renderização da lista de vagas e tenta um refresh se houver timeout.
    private static bool WaitForJobsResults(IWebDriver driver)
    {
        try
        {
            // Wait mais longo para carregamento de resultados de vagas.
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));

            // Retorna true ao encontrar ao menos um card.
            return wait.Until(d => HasAnyJobCard(d));
        }
        catch (WebDriverTimeoutException)
        {
            try
            {
                // Tenta recuperar com refresh da página.
                driver.Navigate().Refresh();

                // Recria wait após refresh.
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));

                // Nova tentativa de encontrar cards.
                return wait.Until(d => HasAnyJobCard(d));
            }
            catch
            {
                // Se ainda assim falhar, informa que não carregou.
                return false;
            }
        }
    }

    // Verifica se existe ao menos um card de vaga na tela.
    private static bool HasAnyJobCard(IWebDriver driver)
    {
        try
        {
            // Primeira tentativa de localizar cards no DOM atual.
            var cards = driver.FindElements(By.CssSelector("li.jobs-search-results__list-item, div.job-card-container, a.job-card-container__link, ul.jobs-search__results-list li"));
            if (cards.Count > 0)
            {
                return true;
            }

            try
            {
                // Faz scroll para baixo para forçar lazy load de itens.
                var js = (IJavaScriptExecutor)driver;
                js.ExecuteScript("window.scrollBy(0, 600);");
            }
            catch
            {
                // Ignora falha de script e continua tentativa.
            }

            // Segunda tentativa após scroll.
            cards = driver.FindElements(By.CssSelector("li.jobs-search-results__list-item, div.job-card-container, a.job-card-container__link, ul.jobs-search__results-list li"));
            return cards.Count > 0;
        }
        catch
        {
            // Em caso de erro no Selenium, considera que não há cards.
            return false;
        }
    }

    // Tenta obter texto de um elemento usando vários seletores em ordem de prioridade.
    private static string GetTextBySelectors(IWebElement root, params string[] selectors)
    {
        // Percorre lista de seletores fallback.
        foreach (var selector in selectors)
        {
            try
            {
                // Busca elemento filho pelo seletor atual.
                var element = root.FindElement(By.CssSelector(selector));

                // Limpa espaços extras do texto encontrado.
                var text = element?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch (NoSuchElementException)
            {
                // Se não encontrou com este seletor, tenta o próximo.
                continue;
            }
        }

        // Se nenhum seletor funcionou, retorna string vazia.
        return string.Empty;
    }

    // Tenta obter um atributo (ex.: href) usando vários seletores em ordem de prioridade.
    private static string GetAttributeBySelectors(IWebElement root, string attributeName, params string[] selectors)
    {
        // Percorre seletores candidatos.
        foreach (var selector in selectors)
        {
            try
            {
                // Busca elemento para leitura do atributo.
                var element = root.FindElement(By.CssSelector(selector));

                // Lê e limpa valor do atributo.
                var value = element?.GetAttribute(attributeName)?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch (NoSuchElementException)
            {
                // Não encontrou com este seletor; continua para o próximo.
                continue;
            }
        }

        // Se nenhum seletor produziu valor válido, retorna vazio.
        return string.Empty;
    }

    // Verifica se o card representa vaga com candidatura simplificada.
    private static bool HasSimplifiedApplication(IWebElement card)
    {
        try
        {
            // Busca badges/textos específicos no card.
            var badgeElements = card.FindElements(By.XPath(".//*[contains(., 'Candidatura simplificada') or contains(@aria-label, 'Candidatura simplificada')]"));
            if (badgeElements.Count > 0)
            {
                return true;
            }

            // Fallback: busca no texto geral do card.
            var text = card.Text;
            return text != null && text.Contains("Candidatura simplificada", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Em caso de erro de leitura, trata como não simplificada.
            return false;
        }
    }

    // Imprime tabela simples no console para visualizar resultado da coleta.
    private static void PrintVagasTabela(List<(string Titulo, string Empresa, string Localizacao, string Link)> vagas)
    {
        // Largura máxima da coluna título.
        const int titleWidth = 40;

        // Largura máxima da coluna empresa.
        const int companyWidth = 30;

        // Largura máxima da coluna localização.
        const int locationWidth = 25;

        // Função local para truncar texto e evitar quebra visual da tabela.
        string Truncate(string value, int width)
        {
            if (string.IsNullOrWhiteSpace(value)) return "".PadRight(width);
            var v = value.Trim();
            if (v.Length <= width) return v.PadRight(width);
            return v.Substring(0, Math.Max(0, width - 1)) + "…";
        }

        // Monta cabeçalho da tabela.
        var header = $"| {"Título".PadRight(titleWidth)} | {"Empresa".PadRight(companyWidth)} | {"Localização".PadRight(locationWidth)} | Link";

        // Monta linha separadora do cabeçalho.
        var separator = $"|-{new string('-', titleWidth)}-|-{new string('-', companyWidth)}-|-{new string('-', locationWidth)}-|------";

        // Escreve cabeçalho no console.
        Console.WriteLine(header);
        Console.WriteLine(separator);

        // Escreve cada vaga como uma linha da tabela.
        foreach (var vaga in vagas)
        {
            Console.WriteLine($"| {Truncate(vaga.Titulo, titleWidth)} | {Truncate(vaga.Empresa, companyWidth)} | {Truncate(vaga.Localizacao, locationWidth)} | {vaga.Link}");
        }
    }
}