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
- Verificação periódica do SSID conectado (intervalo configurável; padrão 10 minutos)
- Registro de eventos de rede por dia e por rede configurada
- Suporte a presença manual, exclusão e restauração de registros automáticos
- Calendário com seleção por dia, intervalo e ações em lote
- Cálculo de ritmo, percentual mensal, projeção e dias necessários para atingir a meta
- Gestão de feriados locais e remotos (BrasilAPI)
- Persistência em SQLite com backups rotativos e restauração pela interface
- Inicialização com o Windows e minimização para a bandeja ao fechar

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
~~~

Tecnologias principais:

- C# / .NET 8
- Windows Forms
- Entity Framework Core + SQLite
- Microsoft.Extensions.DependencyInjection / Logging
- API WLAN nativa do Windows (`wlanapi.dll`)

## Requisitos

- Windows 10/11 x64
- Serviço WLAN ativo no sistema
- [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0) para desenvolvimento
- Inno Setup 6 apenas se for gerar o instalador

> A distribuição release pode ser self-contained; a máquina final não precisa do runtime .NET instalado.

## Executando em desenvolvimento

### 1. Preparar o ambiente

Na raiz do repositório:

~~~powershell
dotnet --list-sdks
dotnet restore PresenceTracker.sln
~~~

Confirme que há um SDK `8.x` instalado. Se `restore` falhar, instale o .NET 8 SDK e reabra o terminal.

### 2. Compilar

~~~powershell
dotnet build PresenceTracker.sln
~~~

### 3. Executar o aplicativo

~~~powershell
dotnet run --project src/PresenceTracker/PresenceTracker.csproj
~~~

Comportamento esperado:

- Abre a janela principal do Presence Tracker
- Uma única instância é permitida; uma segunda execução simplesmente encerra
- Com **Ao fechar → Minimizar para a bandeja** ativo (padrão), o X não encerra o programa: ele continua na bandeja do sistema
- Para encerrar de verdade, use **Sair** no menu da bandeja

### 4. Recarregar alterações de código

O `dotnet run` não recarrega automaticamente após edições. Para testar mudanças:

1. Encerre a instância atual pela bandeja (**Sair**)
2. Pare o processo no terminal com `Ctrl+C`, se ainda estiver ativo
3. Execute novamente `dotnet run --project src/PresenceTracker/PresenceTracker.csproj`

Se o build avisar que `PresenceTracker.exe` está em uso, ainda há uma instância aberta (geralmente na bandeja).

### 5. Testes

~~~powershell
dotnet test PresenceTracker.sln
~~~

### 6. Atalhos úteis em desenvolvimento

| Ação | Comando / caminho |
| --- | --- |
| Só o app | `dotnet run --project src/PresenceTracker/PresenceTracker.csproj` |
| Só testes | `dotnet test PresenceTracker.sln` |
| Banco local | `%LOCALAPPDATA%\PresenceTracker\presence.db` |
| Logs | `%LOCALAPPDATA%\PresenceTracker\logs` |
| Backups | `%LOCALAPPDATA%\PresenceTracker\backups` |

Configurações relevantes na UI:

- **Configurações → Geral**: iniciar com o Windows; minimizar para a bandeja ao fechar
- **Configurações → Redes**: SSIDs presenciais e intervalo de verificação Wi‑Fi (minutos)
- **Configurações → Backup**: pasta, retenção e intervalo de backup
- **Configurações → Datas**: dias úteis, início da semana e feriados

## Publicando: portable vs instalador

Há dois entregáveis distintos:

| Entregável | Como gerar | O que é |
| --- | --- | --- |
| **Portable** | `dotnet publish ... -o artifacts/portable` | EXE único, sem wizard. Basta copiar e executar. |
| **Instalador** | `powershell -File installer/build.ps1` | `PresenceTracker-Setup.exe` com wizard Inno Setup. |

### Versão portable

~~~powershell
dotnet publish src/PresenceTracker/PresenceTracker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/portable
~~~

### Instalador com wizard

Requer [Inno Setup 6](https://jrsoftware.org/isinfo.php). O script `installer/build.ps1` publica o app e compila o setup:

~~~powershell
powershell -File installer/build.ps1
~~~

O instalador fica em `artifacts/installer/PresenceTracker-Setup.exe` e inclui:

- instalação por usuário em `%LOCALAPPDATA%\Programs\Presence Tracker` (sem elevação de administrador)
- **atualização automática** quando já existe uma instalação com o mesmo produto (mesmo `AppId`): reutiliza a pasta, preserva dados e substitui os arquivos
- escolha da pasta de instalação e do grupo do Menu Iniciar (somente na primeira instalação)
- personalização das pastas de **logs** e **backups** (na primeira instalação)
- atalho no Menu Iniciar e opção de atalho na área de trabalho
- opção de **iniciar com o Windows**
- desinstalador em Apps e Recursos e no Menu Iniciar

## Organização de dados locais

~~~text
%LOCALAPPDATA%\PresenceTracker\
├── presence.db
├── backups\          (padrão; pode ser personalizado no instalador ou nas Configurações)
├── logs\             (padrão; pode ser personalizado no instalador)
├── config\
│   └── install.ini   (gerado pelo instalador)
└── ...
~~~

Os backups rodam em intervalos configuráveis e também antes de operações sensíveis (por exemplo, restauração de fábrica). A restauração também pode ser feita em **Configurações → Backup**.

### Recuperação manual de backup

1. Feche o Presence Tracker pela bandeja (**Sair**).
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

- rede cadastrada, ativa e marcada como “considerar presencial”
- evento de conexão (notificação WLAN do Windows) **ou**
- verificação periódica do SSID (Configurações → Redes), quando o SSID atual difere do último registrado

Se o SSID for o mesmo da última verificação, nenhum registro novo é criado. Conexões repetidas no mesmo dia não duplicam a presença. Há proteção para evitar que uma conexão antiga seja interpretada como presença no dia seguinte após suspensão ou hibernação.

## Metas e cálculos

O aplicativo calcula:

- ritmo até hoje
- percentual do mês
- dias necessários para atingir a meta
- projeção de cumprimento com base no planejamento
- histórico por mês

A meta padrão é 40%.

## Diagnóstico rápido

- Nenhuma presença automática: verifique se o SSID está cadastrado, ativo e marcado como presencial
- Wi‑Fi não monitorado: confirme o adaptador e o serviço WLAN do Windows
- Intervalo de checagem: Configurações → Redes → Verificar Wi‑Fi (minutos)
- Problemas de sincronização de feriados: o calendário local continua funcionando sem internet
- Erro de SQLite: consulte os logs e restaure um backup íntegro
- App “não fecha” com o X: comportamento esperado com minimização para a bandeja; use **Sair** na bandeja
- Inicialização com o Windows: Configurações → Geral → Com o Windows

## Limitações conhecidas

- O monitoramento depende do aplicativo estar em execução
- O calendário inicial tem foco em São Paulo; outras localidades exigem ajustes manuais
- Os dados ficam locais no computador e não são sincronizados entre máquinas
- A restauração do banco deve ser feita com cuidado

## Licença

Este repositório está sob a licença informada no arquivo LICENSE.
