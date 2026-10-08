# GitHub Actions Workflows

Este diretório contém os arquivos de workflow do GitHub Actions para o projeto Presence Tracker.

## build.yml

O arquivo `build.yml` é responsável por:

- Construir e testar o projeto
- Rodar regressão de seleção multi-dia do calendário (`CalendarSelectionTests`)
- Rodar regressão de backup/restore (`SqliteBackupServiceTests`)
- Publicar o binário self-contained usado pelo setup
- Compilar o instalador gráfico com Inno Setup (`PresenceTracker-Setup.exe`)
- Publicar o instalador como asset da release `Latest` (download direto do `.exe`, sem ZIP)

O download fica em: https://github.com/andersongni/presence-tracker/releases/latest
