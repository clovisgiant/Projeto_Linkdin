@echo off
setlocal enabledelayedexpansion

REM WebCrawler - Automation Manual
REM Clique duplo para rodar o crawler

echo.
echo ========================================
echo   WebCrawler - Iniciando Automacao
echo   [Release Build - Otimizado]
echo ========================================
echo.

REM Seta variaveis de timezone/locale se necessario
set TZ=America/Sao_Paulo

REM Roda o executavel
cd /d "%~dp0"
WebCrawler.exe

REM Se terminar, aguarda 5 segundos antes de fechar
echo.
echo Automacao finalizada. Janela fechara em 5 segundos...
timeout /t 5 /nobreak

exit /b
