# Presence Tracker

Aplicativo nativo para Windows que registra dias presenciais a partir de conexões às redes Wi-Fi configuradas. O uso cotidiano é local: dados e histórico ficam no computador. A Internet é usada apenas para sincronizar feriados.

## O que o aplicativo faz

- Detecta conexões e desconexões pela Windows Native WLAN API.
- Registra presença automática quando ocorre uma nova conexão a um SSID presencial ativo.
- Permite presença manual e excluir/restaurar detecções automáticas sem apagar os eventos.
- Mantém calendário, planejamento e exceções para férias, day off, feriados e dias não trabalhados.
- Calcula ritmo até hoje, percentual mensal, meta, dias restantes e projeção.
- Guarda metas e dias úteis por mês para preservar o histórico.
- Salva alterações imediatamente em SQLite.
- Mantém cópias do banco e logs rotativos.

## Arquitetura

~~~text
src/
  PresenceTracker/                 WinForms, bandeja e composição da aplicação
  PresenceTracker.Application/     Serviços e contratos
  PresenceTracker.Domain/          Entidades e regras de métricas
  PresenceTracker.Infrastructure/  Feriados remotos/locais e logging
  PresenceTracker.Persistence/     EF Core, SQLite, migration e backup
  PresenceTracker.NetworkMonitoring/ Windows Native WLAN API
tests/
  PresenceTracker.Tests/           Testes unitários do domínio
installer/
  PresenceTracker.iss              Instalador Inno Setup
  build.ps1                        Publicação e criação do setup
docs/
~~~

Tecnologias: C#, .NET 8, Windows Forms, Entity Framework Core, SQLite, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging, Serilog e API WLAN nativa do Windows.

## Requisitos

- Windows 10/11 x64 com serviço WLAN ativo.
- .NET SDK 8 para compilar.
- Inno Setup 6 para gerar o instalador gráfico.
- A máquina de destino não precisa do runtime .NET: a publicação é self-contained.

## Compilar e executar em desenvolvimento

No diretório do projeto:

~~~powershell
dotnet restore PresenceTracker.sln
dotnet build PresenceTracker.sln
dotnet run --project src/PresenceTracker/PresenceTracker.csproj
~~~

O Presence Tracker fica na bandeja. Use Abrir para mostrar a janela e Sair para encerrar o monitoramento.

## Publicar e gerar o instalador

A publicação oficial usa win-x64, single-file e self-contained:

~~~powershell
dotnet publish src/PresenceTracker/PresenceTracker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish/win-x64
~~~

Com o Inno Setup 6 instalado, execute installer/build.ps1. O setup final é artifacts/installer/PresenceTracker-Setup.exe. O instalador é por usuário, cria atalhos e configura a inicialização com o Windows. Uma atualização substitui os arquivos do programa sem tocar na pasta de dados.

O setup mostra as telas de boas-vindas, localização, instalação e conclusão. A desinstalação remove o aplicativo e seus atalhos; banco, backups e logs em %LOCALAPPDATA%\PresenceTracker são preservados.

## Instalação e primeira execução

Execute PresenceTracker-Setup.exe e siga as telas. A rede presencial padrão é CORP. Para cadastrar redes da empresa, abra Configurações → Redes. Os campos são salvos enquanto são editados.

A base local inicial inclui feriados de 2026 e 2027. Em segundo plano, o programa tenta buscar feriados do ano atual e do próximo. A sincronização não bloqueia o uso e, se falhar ou estiver sem Internet, os feriados já salvos são mantidos. Também é possível sincronizar manualmente em Configurações → Feriados.

A fonte remota é BrasilAPI, consultada por ano com o filtro estadual de São Paulo. O calendário local complementa a resposta para São Paulo e não importa Carnaval como feriado legal automático. Feriados municipais e estaduais adicionais podem ser cadastrados pela interface. As correções manuais prevalecem sobre uma sincronização posterior.

## Monitoramento Wi-Fi

A aplicação registra notificações WLAN de conexão concluída e desconexão e consulta SSID/interface pela API nativa do Windows. Ela não analisa saída de netsh.

Só uma notificação nova de conexão a um SSID presencial ativo cria presença automática. Não há duração mínima. Conexões repetidas ficam no histórico técnico, mas a métrica conta no máximo um dia.

O aplicativo cria uma amostra silenciosa do estado inicial para não converter uma conexão mantida durante suspensão, hibernação ou a virada da meia-noite em nova presença. Antes de suspender, invalida a conexão anterior; uma reconexão WLAN recebida ao voltar é registrada. Se o programa iniciar com a rede já conectada e o Windows não enviar nova notificação, essa conexão inicial não é inferida como presença. Use Presencial manual quando necessário. Essa regra evita contar no novo dia uma conexão iniciada no dia anterior.

Eventos de rede aparecem nos detalhes do dia. SSIDs não são enviados a um servidor.

## Calendário e classificações

- Clique em um dia para selecioná-lo.
- Ctrl + clique seleciona ou remove dias alternados.
- Shift + clique seleciona um intervalo.
- Use a barra de ações para aplicar uma classificação a todos os dias selecionados.
- Clique duas vezes em um dia para ver presenças e eventos de rede.
- Presença manual pode ser removida em lote. Presenças automáticas são excluídas/restauradas nos detalhes e permanecem armazenadas.
- Operações em lote usam transação SQLite.

Uma exceção (feriado, férias, day off ou dia não trabalhado) retira o dia do cálculo. Presença manual ou automática conta quando não há exceção. Planejamento é previsão; quando chega uma presença real, o dia deixa de ser planejamento ativo. Planejamentos passados não entram na projeção.

## Metas e cálculos

A meta padrão é 40%. A interface aceita valores maiores que 0% e até 100%, inclusive decimais. As metas rápidas são 20%, 40% e 60%.

- Ritmo até hoje: presenças válidas divididas pelos dias úteis trabalháveis já decorridos. O dia atual só entra quando existe resultado.
- Percentual do mês: presenças válidas divididas pelo total de dias úteis trabalháveis do mês.
- Dias necessários: Ceiling(total de dias úteis trabalháveis × meta).
- Projeção: dias realizados mais dias futuros planejados, sem duplicar datas.
- Os percentuais realizados não passam de 100%.
- A média histórica é simples, sem ponderar pela quantidade de dias úteis.
- O histórico abre com os 12 meses preenchidos mais recentes e permite carregar meses anteriores.

Meta e semana de trabalho são salvas em snapshot mensal. Mudanças no mês corrente atualizam esse mês; meses encerrados mantêm valores históricos.

## Feriados

A base local inclui feriados nacionais e o calendário de São Paulo: aniversário da cidade, Sexta-feira Santa, Corpus Christi e 9 de julho. 20 de novembro é feriado nacional. A interface permite selecionar ano, adicionar, editar, ativar/desativar e remover feriados dos escopos nacional, estadual, municipal e personalizado.

A sincronização usa GET https://brasilapi.com.br/api/feriados/v1/{ano}?uf=SP. O primeiro uso tenta sincronizar os anos atual e seguinte em segundo plano. A indisponibilidade remota é registrada; a base local continua disponível.

## Banco de dados e backups

SQLite:

~~~text
%LOCALAPPDATA%\PresenceTracker\presence.db
~~~

Pastas:

~~~text
%LOCALAPPDATA%\PresenceTracker\backups\
%LOCALAPPDATA%\PresenceTracker\logs\
%LOCALAPPDATA%\PresenceTracker\config\
~~~

Um backup é criado a cada 24 horas e antes de ações em lote grandes ou remoção de classificações. A retenção padrão é 30 arquivos, configurável entre 1 e 365. A cópia usa a API SQLite e é executada fora da interface.

### Restauração manual

1. Encerre Presence Tracker pelo menu da bandeja.
2. No Explorador, abra %LOCALAPPDATA%\PresenceTracker\backups.
3. Copie o backup desejado.
4. Abra %LOCALAPPDATA%\PresenceTracker e substitua presence.db pela cópia.
5. Abra o Presence Tracker novamente.

Guarde uma cópia do banco atual antes de substituir. Se houver corrupção, encerre o aplicativo, restaure um backup íntegro e confira os logs.

## Logs

Os logs ficam em %LOCALAPPDATA%\PresenceTracker\logs. Há rotação diária e por tamanho: no máximo 10 arquivos de aproximadamente 10 MB cada, mantendo os mais novos. O nível pode ser alterado em Configurações → Geral.

## Testes

~~~powershell
dotnet test PresenceTracker.sln
~~~

Os testes cobrem dias úteis, feriados por escopo, exceções, presenças manual e automática, exclusão sem apagar o evento, metas e Ceiling, planejamento, dia corrente, limites de mês/ano e média histórica.

## Diagnóstico

- Nenhuma presença automática: confira se o SSID está cadastrado e ativo. O aplicativo precisa receber a notificação de conexão WLAN.
- Wi-Fi não monitorado: confirme se o adaptador e o serviço WLAN do Windows estão ativos. Presenças manuais continuam disponíveis.
- Sincronização indisponível: o calendário local continua ativo; tente novamente depois.
- Erro SQLite/inicialização: feche o app, faça uma cópia de presence.db, verifique os logs e restaure um backup íntegro.
- Inicialização automática: confira Configurações → Geral → Iniciar automaticamente.

## Limitações conhecidas

- O monitoramento precisa estar em execução para observar eventos; não é possível datar com segurança conexões ocorridas antes da primeira execução.
- O calendário inicial estadual/municipal é de São Paulo. Outras localidades precisam de correção manual.
- Os dados não são replicados entre computadores.
- A restauração do backup é manual nesta versão.
