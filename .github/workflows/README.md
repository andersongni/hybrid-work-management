# GitHub Actions Workflows

Este diretório contém os arquivos de workflow do GitHub Actions para o projeto Presence Tracker.

## build.yml

O arquivo `build.yml` é responsável por:

- Construir e testar o projeto
- Publicar o binário self-contained usado pelo setup
- Compilar o instalador gráfico com Inno Setup (`PresenceTracker-Setup.exe`)
- Gerar a versão portable (EXE único)
- Fazer upload dos dois artefatos no GitHub Actions
