# Versionamento

**Atual:** `1.7.7`
**Arquivo/projeto atual:** `app/AYVUNoLag.csproj`

## Historico

### 1.7.7 - 2026-05-31

- Tipo: release
- Autor: Codex
- Motivo: estica o Monitor em tempo real de ponta a ponta com os cards lado a lado e altera MSI Mode para aplicação individual por dispositivo.
- Breaking changes: nenhum
- Validação: `dotnet build app/AYVUNoLag.csproj -c Release` → 0 erros, 2 avisos de dependência `NSec.Cryptography`.

### 1.7.6 - 2026-05-31

- Tipo: release
- Autor: Codex
- Motivo: reduz o intervalo de atualização do monitor para 500 ms, mantendo o layout com Monitor em tempo real centralizado no topo.
- Breaking changes: nenhum
- Validação: `dotnet build app/AYVUNoLag.csproj -c Release` → 0 erros, 2 avisos de dependência `NSec.Cryptography`.

### 1.7.5 - 2026-05-31

- Tipo: release
- Autor: Codex
- Motivo: centraliza o Monitor em tempo real no topo e reorganiza as linhas seguintes como Ping/MSI/Jogos e DNS/Diagnóstico/Otimizações/Log.
- Breaking changes: nenhum
- Validação: `dotnet build app/AYVUNoLag.csproj -c Release` → 0 erros, 2 avisos de dependência `NSec.Cryptography`.

### 1.7.4 - 2026-05-31

- Tipo: release
- Autor: Codex
- Motivo: reorganiza o layout principal em 2 linhas e move Ping Médio, Jitter, Packet Loss e Melhor Ping para dentro do Monitor em tempo real.
- Breaking changes: nenhum
- Validação: `dotnet build app/AYVUNoLag.csproj -c Release` → 0 erros, 2 avisos de dependência `NSec.Cryptography`.

### 1.7.3 - 2026-05-31

- Tipo: release
- Autor: Codex
- Motivo: reorganiza o monitor em tempo real para exibir CPU, RAM, GPU e Disco lado a lado, reduzindo altura vertical.
- Breaking changes: nenhum
- Validação: `dotnet build app/AYVUNoLag.csproj -c Release` → 0 erros, 2 avisos de dependência `NSec.Cryptography`.

### 1.7.2 - 2026-05-31

- Tipo: release
- Autor: Codex
- Motivo: publica fixes locais de sparkline/build e layout com MSI Mode ao lado de Amostras de Ping.
- Breaking changes: nenhum
- Validação: `dotnet publish app/AYVUNoLag.csproj -c Release -r win-x64` antes da release.

### 1.7.1-layout-fix - 2026-05-31

- Tipo: ajuste local de UI
- Autor: Codex
- Motivo: usuario pediu colocar a box de MSI Mode ao lado de Amostras de Ping.
- Arquivo alterado: `app/MainWindow.xaml`
- Breaking changes: nenhum
- Validação: `dotnet build app/AYVUNoLag.csproj -c Release` → 0 erros, 2 avisos de dependência `NSec.Cryptography`.
- Ajustes:
  - Row de Amostras de Ping passou a ter uma coluna dedicada para MSI Mode.
  - Painel MSI Mode ativo foi movido para o lado de Amostras de Ping.
  - Painel MSI Mode antigo da Row inferior ficou oculto como legado visual.
  - Row inferior agora fica com DNS, Otimizações e Log.

### 1.7.1-source-fix - 2026-05-31

- Tipo: fix local pós-release
- Autor: Codex
- Motivo: finalizar alteração interrompida após release; build atual falhava porque os helpers de sparkline não existiam.
- Arquivo alterado: `app/MainWindow.xaml.cs`
- Breaking changes: nenhum
- Validação: `dotnet build app/AYVUNoLag.csproj -c Release` → 0 erros, 2 avisos de dependência `NSec.Cryptography`.
- Ajustes:
  - Adicionado helper `Enqueue` para manter histórico dos sparklines.
  - Adicionado helper `DrawSparkline` para renderizar os gráficos em Canvas.
  - Adicionado `using System.Windows.Shapes`.
  - Removido `using System.Diagnostics` duplicado.

### 1.7.1 - 2026-05-31

- Tipo: release
- Autor: release.ps1
- Motivo: Cleanup: remove codigo morto do MSI Mode no botao Otimizar (agora exclusivo do painel dedicado)

### 1.7.0 - 2026-05-31

- Tipo: release
- Autor: release.ps1
- Motivo: MSI Mode reformulado: painel dedicado GPU + Rede com toggle individual, removido do ciclo Otimizar, banner so quando estado muda de verdade

### 1.6.2 - 2026-05-31

- Tipo: release
- Autor: release.ps1
- Motivo: UX: painel GPU Mode dedicado — toggle MSI Mode com status da GPU, botao Aplicar independente e banner de reinicializacao

### 1.6.1 - 2026-05-31

- Tipo: release
- Autor: release.ps1
- Motivo: UX: banner de reinicializacao apos MSI Mode — botoes Reiniciar agora (15s) e Reiniciar depois

### 1.6.0 - 2026-05-31

- Tipo: release
- Autor: release.ps1
- Motivo: Feature: 4 otimizacoes de FPS — Fullscreen Optimizations off, Visual Effects minimo, Trim RAM background, MSI Mode GPU

### 1.5.0 - 2026-05-31

- Tipo: release
- Autor: release.ps1
- Motivo: Feature: 3 novas otimizacoes (GPU Priority, HAGS, TCP Timestamps) + DNS real 1.1.1.1 substituindo placeholder

### 1.4.2 - 2026-05-29

- Tipo: release
- Autor: release.ps1
- Motivo: Remove painel GPU Scaling do programa

### 1.4.1 - 2026-05-29

- Tipo: release
- Autor: release.ps1
- Motivo: GPU Scaling: troca RadioButtons por ComboBox dropdown, layout responsivo

### 1.4.0 - 2026-05-29

- Tipo: release
- Autor: release.ps1
- Motivo: Adiciona painel GPU Scaling (Full Panel, Preserve Aspect Ratio, Centered) independente do otimizador


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
