# Versionamento

**Atual:** `0.1.0`
**Arquivo/projeto atual:** `app/AYVUNoLag.csproj`

## Historico

### 0.1.0 - 2026-05-27

- Tipo: inicial
- Autor: Codex
- Motivo: criacao do MVP Windows do AYVU NoLag.
- Arquivos principais:
  - `app/AYVUNoLag.csproj`
  - `app/MainWindow.xaml`
  - `app/MainWindow.xaml.cs`
  - `app/Models/DiagnosticModels.cs`
  - `app/Services/NetworkDiagnosticService.cs`
  - `app/Services/GameProcessService.cs`
  - `app/Services/LocalBoostService.cs`
- Validacao: `dotnet build -c Release` com 0 erros e 0 avisos.

## Notas

- v0.1.0 e um otimizador local, nao um tunel gamer.
- Mudancas que alterem comportamento de rede real devem exigir revisao explicita e documentacao de rollback.
