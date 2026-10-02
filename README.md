# Presence Tracker

Aplicativo desktop para Windows que ajuda a controlar presença em home office ou ambiente corporativo com base em redes Wi‑Fi e em regras de calendário, metas e exceções.

O projeto foi pensado para funcionar localmente no computador do usuário: os dados ficam no próprio Windows, as redes monitoradas são definidas manualmente e a sincronização de feriados é opcional e usa a internet apenas para complementar o calendário.

## Visão geral

O Presence Tracker registra:

- presenças automáticas por conexão de SSID configurado
- presença manual por dia
- exceções como feriados, férias, day off e dias não trabalhados
- metas mensais e projeções de cumprimento
- histórico persistido em SQLite
- backups automáticos e logs de operação

Ele foi construído como um utilitário desktop com WinForms, com foco em uso local e em monitoramento contínuo da rede Wi‑Fi do Windows.

## Principais recursos

- Monitoramento de conexão e desconexão via API nativa do Windows WLAN
- Registro de eventos de rede por dia e por rede configurada
- Suporte a presença manual, exclusão e restauração de registros automáticos
- Calendário com seleção por dia, intervalo e ações em lote
- Cálculo de ritmo, percentual mensal, projeção e dias necessários para atingir a meta
- Gestão de feriados locais e remotos (BrasilAPI)
- Persistência em SQLite com backups rotativos
- Backup periódico e logs configuráveis
- Inicialização automática com o Windows

## Arquitetura

~~~text
src/
  PresenceTracker/                 WinForms, UI principal e bandeja do sistema
  PresenceTracker.Application/     Serviços e regras de aplicação
  PresenceTracker.Domain/          Entidades, calendário e métricas
  PresenceTracker.Infrastructure/  Feriados, logging e utilitários de infraestrutura
  PresenceTracker.Persistence/     EF Core, SQLite, migrações e backup
  PresenceTracker.NetworkMonitoring/ Monitoramento Wi-Fi do Windows

tests/
  PresenceTracker.Tests/           Testes automatizados de domínio e persistência

installer/
  PresenceTracker.iss              Instalador Inno Setup
  build.ps1                        Script de publicação e geração do setup

docs/
  ...
~~~

Tecnologias principais:

- C#
- .NET 8
- Windows Forms
- Entity Framework Core
- SQLite
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging
- API WLAN nativa do Windows

## Requisitos

- Windows 10/11 x64
- Serviço WLAN ativo no sistema
- .NET SDK 8 para compilar o projeto no ambiente de desenvolvimento
- Inno Setup 6 para gerar o instalador

> A máquina final não precisa do runtime .NET instalado, pois a distribuição pode ser publicada como self-contained.

## Executando em desenvolvimento

Na raiz do repositório:

~~~powershell
dotnet restore PresenceTracker.sln
dotnet build PresenceTracker.sln
dotnet run --project src/PresenceTracker/PresenceTracker.csproj
~~~

A aplicação roda na bandeja do sistema. O menu de bandeja permite abrir a janela principal e encerrar o monitoramento quando necessário.

## Publicando e gerando o instalador

Para publicar a build de release em Windows:

~~~powershell
dotnet publish src/PresenceTracker/PresenceTracker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish/win-x64
~~~

Depois, com o Inno Setup instalado, execute:

~~~powershell
powershell -File installer/build.ps1
~~~

O instalador gerado fica em `artifacts/installer/` e o aplicativo cria atalhos e pode ser configurado para iniciar junto com o Windows.

## Organização de dados locais

Os dados do usuário ficam em:

~~~text
%LOCALAPPDATA%\PresenceTracker\
├── presence.db
├── backups\
├── logs\
├── config\
└── ...
~~~

O banco é SQLite e os backups são realizados automaticamente em intervalos configurados, além de antes de operações mais sensíveis, como remoções em lote ou grandes alterações de calendário.

### Recuperação manual de backup

1. Feche o Presence Tracker pela bandeja.
2. Acesse `%LOCALAPPDATA%\PresenceTracker\backups`.
3. Copie o backup desejado para outra pasta.
4. Substitua `presence.db` pela cópia restaurada.
5. Abra o aplicativo novamente.

> Sempre mantenha uma cópia do banco atual antes de restaurar um backup.

## Feriados e calendário

A aplicação suporta:

- feriados nacionais, estaduais, municipais e personalizados
- sincronização com BrasilAPI para o estado de São Paulo
- complemento local do calendário
- exceções que removem dias do cálculo de presença

A sincronização é opcional, não bloqueia o uso do programa e continua funcionando com o banco local quando a internet não está disponível.

## Regras de presença

A presença automática depende de:

- rede configurada como ativa
- rede marcada como contando presença
- notificação de conexão do Windows para aquele SSID

Conexões repetidas são registradas em eventos técnicos, mas não geram duplicação de presença para o mesmo dia. Também há proteção para evitar que uma conexão antiga seja interpretada como presença no dia seguinte após suspensão ou hibernação do sistema.

## Metas e cálculos

O aplicativo calcula:

- ritmo até hoje
- percentual do mês
- dias necessários para atingir a meta
- projeção de cumprimento
- histórico por mês e comparação com meses anteriores

A meta padrão é 40% e o sistema aceita valores em porcentagem dentro do escopo do produto.

## Testes

Para executar a suíte de testes:

~~~powershell
dotnet test PresenceTracker.sln
~~~

Os testes cobrem regras de presença, feriados, exceções, cálculos de meta, planejamento e persistência.

## Diagnóstico rápido

- Nenhuma presença automática: verifique se o SSID está cadastrado e ativo.
- Wi‑Fi não monitorado: confirme que o adaptador e o serviço WLAN do Windows estão funcionando.
- Problemas de sincronização: o calendário local continua funcionando sem internet.
- Erro de SQLite: verifique os logs e restaure um backup íntegro.
- Inicialização com o Windows: confira as opções em Configurações → Geral.

## Limitações conhecidas

- O monitoramento depende do aplicativo estar em execução.
- O calendário inicial tem foco em São Paulo; outras localidades exigem ajustes manuais.
- Os dados ficam locais no computador e não são sincronizados entre máquinas.
- A restauração do banco é manual e deve ser feita com cuidado.

## Licença

Este repositório está sob a licença informada no arquivo LICENSE.
