-- ============================================================
-- RELATÓRIO DE CANDIDATURAS - ProjetoDb
-- Rodar: psql -h localhost -U postgres -d ProjetoDb -f relatorio_candidaturas.sql
-- ============================================================

\echo ''
\echo '=== CANDIDATURAS ENVIADAS COM SUCESSO ==='
c

\echo ''
\echo '=== VAGAS INDISPONIVEIS / BLOQUEADAS ==='
SELECT titulo, empresa, motivo_indisponibilidade,
       to_char(data_indisponibilidade, 'DD/MM/YYYY HH24:MI') AS em
FROM vagas
WHERE candidatura_indisponivel = TRUE
ORDER BY data_indisponibilidade DESC;

\echo ''
\echo '=== ENVIADAS SEM CONFIRMACAO DE SUCESSO ==='
SELECT titulo, empresa,
       to_char(data_candidatura, 'DD/MM/YYYY HH24:MI') AS candidatura_em
FROM vagas
WHERE candidatura_enviada = TRUE
  AND candidatura_enviada_sucesso = FALSE
  AND candidatura_indisponivel = FALSE
ORDER BY data_candidatura DESC;

\echo ''
\echo '=== RESUMO GERAL POR STATUS ==='
SELECT
  COUNT(*)                                                                        AS total,
  SUM(CASE WHEN candidatura_enviada_sucesso THEN 1 ELSE 0 END)                   AS sucesso,
  SUM(CASE WHEN candidatura_enviada
            AND NOT candidatura_enviada_sucesso
            AND NOT candidatura_indisponivel THEN 1 ELSE 0 END)                  AS enviada_sem_confirmacao,
  SUM(CASE WHEN candidatura_indisponivel THEN 1 ELSE 0 END)                      AS indisponiveis,
  SUM(CASE WHEN NOT candidatura_enviada
            AND NOT candidatura_indisponivel THEN 1 ELSE 0 END)                  AS pendentes
FROM vagas;

\echo ''
\echo '=== TODAS AS VAGAS (VISAO COMPLETA) ==='
SELECT titulo, empresa, localizacao,
       candidatura_enviada          AS enviada,
       candidatura_enviada_sucesso  AS sucesso,
       candidatura_indisponivel     AS indisponivel,
       motivo_indisponibilidade     AS motivo,
       to_char(data_insercao,          'DD/MM HH24:MI') AS inserida_em,
       to_char(data_candidatura,       'DD/MM HH24:MI') AS candidatura_em,
       to_char(data_envio_sucesso,     'DD/MM HH24:MI') AS sucesso_em,
       to_char(data_indisponibilidade, 'DD/MM HH24:MI') AS indisponivel_em
FROM vagas
ORDER BY data_insercao DESC;

\echo ''
\echo '=== ULTIMAS 50 ETAPAS DE CANDIDATURA (LOG DETALHADO) ==='
SELECT link, etapa, sucesso, detalhe,
       to_char(criado_em, 'DD/MM HH24:MI') AS em
FROM candidatura_etapas
ORDER BY criado_em DESC
LIMIT 50;
