# AppLinkdin Mobile (Expo)

Versao mobile do dashboard AppLinkdin, desenvolvida com Expo + React Native + TypeScript.

## O que foi implementado

- Dashboard mobile com visual premium e layout nativo
- Componentes reutilizaveis para cards, status, relogio de runtime e lista de vagas
- Donut chart para distribuicao de status (sucesso, indisponiveis, pendentes)
- Polling automatico de dados a cada 30 segundos
- Pull-to-refresh para atualizacao manual
- Integracao com os endpoints:
  - /api/dashboard
  - /api/crawler-status

## Estrutura principal

- src/screens/DashboardScreen.tsx
- src/components/SummaryCard.tsx
- src/components/StatusBadge.tsx
- src/components/RuntimeClockCard.tsx
- src/components/DonutChart.tsx
- src/components/JobListSection.tsx
- src/services/api.ts
- src/hooks/useDashboardData.ts
- src/constants/theme.ts

## Como rodar

1. Instale dependencias:

   npm install

2. Inicie o Expo:

   npm run start

3. Teste com Expo Go no celular ou emulador.

## Teste sem Expo Go com EAS

O projeto foi preparado para gerar APK de teste via EAS Build.

Arquivos configurados:

- eas.json
- app.json com package Android e bundle identifier iOS
- expo-dev-client instalado para build de desenvolvimento

### Fluxos disponiveis

- Desenvolvimento com dev client:

   npm run eas:build:development

- APK de preview para instalar direto no celular:

   npm run eas:build:preview

- Build de producao Android (AAB):

   npm run eas:build:production

### Primeiro uso do EAS

1. Fazer login na Expo:

    npx eas-cli login

2. Rodar o build desejado:

    npm run eas:build:preview

3. Ao final, a Expo retorna o link para baixar o APK.

### Observacao importante

Para teste rapido no celular real, o melhor caminho e o perfil preview, porque ele gera APK e nao depende do Expo Go.

## URL da API no mobile

A configuracao atual usa:

- Android emulator: http://10.0.2.2:4000
- iOS/default: http://localhost:4000

Se for testar em celular fisico, altere API_BASE_URL em src/services/api.ts para o IP da sua maquina, por exemplo:

http://192.168.0.100:4000

## Proximos passos sugeridos

- Adicionar navegacao por abas
- Criar tela de detalhes da vaga
- Configurar autenticacao
- Padronizar tokens de design para modo claro/escuro
