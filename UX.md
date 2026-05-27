# AYVU NoLag — UX/UI Design

**Versão:** 1.0.0  
**Data:** 2026-05-27  
**Autor:** Claude

---

## Princípios de Design

1. **Honestidade primeiro** — métricas reais, sem números inflados
2. **Resultado visível** — o usuário deve ver diferença antes/depois
3. **Reversível sempre** — toda ação tem um caminho de volta
4. **Rápido de usar** — diagnóstico em 1 clique, otimização em 2
5. **Visual AYVU** — escuro, clean, accent laranja (#BF6247 ou tom gaming)

---

## Paleta de Cores

| Token | Valor | Uso |
|---|---|---|
| `bg-primary` | `#0A0A0A` | Fundo principal |
| `bg-surface` | `#141414` | Cards e painéis |
| `bg-elevated` | `#1E1E1E` | Hover, inputs |
| `border` | `#2A2A2A` | Divisores |
| `accent` | `#FF4400` | CTA, status ativo, destaques |
| `accent-soft` | `#FF440020` | Background de badge de status |
| `text-primary` | `#F0F0F0` | Texto principal |
| `text-secondary` | `#888888` | Labels, subtítulos |
| `success` | `#22C55E` | Ping bom, otimização aplicada |
| `warning` | `#F59E0B` | Ping médio, atenção |
| `danger` | `#EF4444` | Ping alto, erro, packet loss |

---

## Layout Geral

```
┌──────────────────────────────────────────────────────┐
│  [●] AYVU NoLag          [status: ativo]  [─][□][✕] │
├──────────┬───────────────────────────────────────────┤
│          │                                           │
│  NAV     │           CONTENT AREA                   │
│          │                                           │
│ Dashboard│                                           │
│ Diagnós. │                                           │
│ Otimizar │                                           │
│ Jogos    │                                           │
│ DNS      │                                           │
│ Relatório│                                           │
│          │                                           │
│──────────│                                           │
│ Licença  │                                           │
└──────────┴───────────────────────────────────────────┘
```

- Largura mínima: 900px / Altura mínima: 600px
- Nav lateral: 200px fixo, fundo `bg-surface`
- Logo AYVU no topo da nav + nome do produto
- Status pill no header: "Monitorando" (verde) / "Parado" (cinza)

---

## Tela 1 — Dashboard (tela inicial)

**Objetivo:** visão geral instantânea do estado da conexão e sistema

```
┌─────────────────────────────────────────────────────┐
│  Bom dia, jogador.          [▶ Diagnosticar Agora]  │
│  Última análise: há 5 min                           │
├──────────┬──────────┬──────────┬────────────────────┤
│  PING    │  JITTER  │  LOSS    │  RAM LIVRE          │
│  42ms    │  3ms     │  0.2%    │  4.2 GB             │
│  ● Ótimo │  ● Ótimo │  ● Ótimo │  ● Suficiente       │
├──────────┴──────────┴──────────┴────────────────────┤
│  GRÁFICO DE PING AO VIVO (últimos 60s)              │
│  ▁▂▁▁▂▁▃▁▂▁▁▁▂▁▁▂▃▁▁▂                              │
├─────────────────────────────────────────────────────┤
│  OTIMIZAÇÕES ATIVAS                                 │
│  ✓ DNS: Cloudflare (1.1.1.1)   ✓ Plano: Ultimate   │
│  ✓ Nagle: Desativado           ✓ QoS: Removido     │
├─────────────────────────────────────────────────────┤
│  JOGO DETECTADO                                     │
│  🎮 CS2 — prioridade Alta — CPU: 38%               │
└─────────────────────────────────────────────────────┘
```

**Cards de métricas:**
- Fundo `bg-surface`, borda `border`
- Valor em fonte grande (32px bold)
- Label de qualidade com cor semântica (verde/amarelo/vermelho)
- Thresholds de ping: ≤60ms Ótimo | 61-120ms Médio | >120ms Alto

---

## Tela 2 — Diagnóstico

**Objetivo:** análise completa da conexão com resultado por endpoint

```
┌─────────────────────────────────────────────────────┐
│  Diagnóstico de Conexão              [▶ Iniciar]    │
├─────────────────────────────────────────────────────┤
│  ENDPOINTS TESTADOS                    ↕ Editar     │
│                                                     │
│  ● Valve (CS2/BR)      ping: 38ms  jitter: 2ms  ✓  │
│  ● Riot (Valorant/BR)  ping: 44ms  jitter: 4ms  ✓  │
│  ● Cloudflare          ping: 12ms  jitter: 1ms  ✓  │
│  ● Google              ping: 14ms  jitter: 2ms  ✓  │
│  ● 8.8.4.4             ping: 16ms  jitter: 2ms  ✓  │
│                                                     │
├─────────────────────────────────────────────────────┤
│  TRACEROUTE — Valve (CS2/BR)                        │
│  1  192.168.1.1      1ms                            │
│  2  10.0.0.1         8ms                            │
│  3  177.x.x.x        22ms                           │
│  4  ...              38ms   ← servidor              │
├─────────────────────────────────────────────────────┤
│  DNS ATUAL                                          │
│  Provedor: Tim (189.x.x.x)   Resolução: 28ms        │
│  [→ Ir para DNS Optimizer]                          │
└─────────────────────────────────────────────────────┘
```

---

## Tela 3 — Otimizações

**Objetivo:** lista de otimizações com toggle e status de aplicação

```
┌─────────────────────────────────────────────────────┐
│  Otimizações              [▶ Aplicar Selecionadas]  │
├─────────────────────────────────────────────────────┤
│  REDE                                               │
│  [✓] Desativar Nagle          ● Aplicado   [Reverter]│
│  [✓] Remover reserva QoS      ● Aplicado   [Reverter]│
│  [ ] Reset Winsock            ○ Pendente   [Aplicar] │
│  [ ] Reset TCP/IP             ○ Pendente   [Aplicar] │
│                                                     │
│  SISTEMA                                            │
│  [✓] Plano Ultimate           ● Aplicado   [Reverter]│
│  [✓] Limpar RAM               ○ Manual     [Executar]│
│  [ ] Desativar Xbox DVR       ○ Pendente   [Aplicar] │
│                                                     │
│  PRIVACIDADE / TELEMETRIA                           │
│  [ ] Desativar DiagTrack      ○ Pendente   [Aplicar] │
│  [ ] Flush DNS                ○ Manual     [Executar]│
├─────────────────────────────────────────────────────┤
│  ⚠ Algumas ações requerem reinício                  │
│  ⚠ Admin necessário para ações marcadas com 🔒      │
└─────────────────────────────────────────────────────┘
```

**Regras visuais:**
- Badge verde "Aplicado" + botão "Reverter" quando ativo
- Badge cinza "Pendente" + botão "Aplicar" quando inativo
- Badge âmbar "Manual" para ações pontuais (sem toggle)
- Ícone 🔒 em ações que requerem admin

---

## Tela 4 — Jogos

**Objetivo:** detectar jogos ativos e aplicar perfis de prioridade

```
┌─────────────────────────────────────────────────────┐
│  Jogos e Processos                    [+ Novo Perfil]│
├─────────────────────────────────────────────────────┤
│  PROCESSOS DETECTADOS AGORA                         │
│                                                     │
│  🎮 cs2.exe             CPU: 42%  RAM: 1.8GB        │
│     Prioridade: Alta ✓            [Editar Perfil]   │
│                                                     │
│  🎮 steam.exe           CPU: 0.8% RAM: 180MB        │
│     Prioridade: Normal            [Criar Perfil]    │
│                                                     │
├─────────────────────────────────────────────────────┤
│  PERFIS SALVOS                                      │
│                                                     │
│  CS2              cs2.exe              Alta    [✓]  │
│  Valorant         VALORANT-Win64...    Alta    [✓]  │
│  League of Legends LeagueClient.exe   AbvNorm [✓]  │
│  Fortnite         FortniteClient...    Alta    [ ]  │
│                                                     │
│  Auto-aplicar ao detectar processo: [ON]            │
└─────────────────────────────────────────────────────┘
```

---

## Tela 5 — DNS Optimizer

**Objetivo:** comparar e aplicar o DNS mais rápido para o usuário

```
┌─────────────────────────────────────────────────────┐
│  DNS Optimizer                     [▶ Testar Todos] │
├─────────────────────────────────────────────────────┤
│  RESULTADO DO TESTE                                 │
│                                                     │
│  🥇 Cloudflare    1.1.1.1      8ms   ← mais rápido  │
│  🥈 Google        8.8.8.8      14ms                 │
│  🥉 Quad9         9.9.9.9      19ms                 │
│     OpenDNS       208.67.x     24ms                 │
│     ISP Atual     189.x.x.x    31ms  ← em uso       │
│                                                     │
├─────────────────────────────────────────────────────┤
│  DNS ATUAL: Tim (189.x.x.x)                         │
│                                                     │
│  [✓ Aplicar Cloudflare 1.1.1.1]  [← Reverter DNS]  │
│                                                     │
│  ℹ Aplicar altera o DNS do adaptador ativo.         │
│    Reversão restaura o DNS original exato.          │
└─────────────────────────────────────────────────────┘
```

---

## Tela 6 — Relatório Antes/Depois

**Objetivo:** mostrar o impacto das otimizações com comparativo visual

```
┌─────────────────────────────────────────────────────┐
│  Relatório de Sessão              [↓ Exportar HTML] │
├────────────────────────┬────────────────────────────┤
│  ANTES                 │  DEPOIS                    │
├────────────────────────┼────────────────────────────┤
│  Ping: 68ms            │  Ping: 42ms  ▼ -38%  ✓    │
│  Jitter: 12ms          │  Jitter: 3ms ▼ -75%  ✓    │
│  Packet Loss: 1.2%     │  Loss: 0.2%  ▼ -83%  ✓    │
│  RAM Livre: 2.1GB      │  RAM: 4.2GB  ▲ +100% ✓    │
│  DNS: 31ms             │  DNS: 8ms    ▼ -74%  ✓    │
├────────────────────────┴────────────────────────────┤
│  OTIMIZAÇÕES APLICADAS NESTA SESSÃO                 │
│  ✓ DNS alterado para Cloudflare                     │
│  ✓ Nagle desativado                                 │
│  ✓ QoS removido                                     │
│  ✓ RAM limpa: 2.1 GB liberados                      │
│  ✓ Plano Ultimate ativado                           │
│  ✓ Prioridade Alta: cs2.exe                         │
├─────────────────────────────────────────────────────┤
│  Sessão: 2026-05-27 14:32  Duração: 1h 42min       │
└─────────────────────────────────────────────────────┘
```

---

## Micro-interações

| Elemento | Comportamento |
|---|---|
| Botão "Aplicar" | Loading spinner → badge "Aplicado" com animação fade-in |
| Card de ping | Cor muda dinamicamente (verde/amarelo/vermelho) conforme valor |
| Gráfico de ping | Atualização suave a cada 2s, sem flash |
| Detecção de jogo | Toast no canto inferior: "CS2 detectado — perfil aplicado" |
| Erro | Toast vermelho com mensagem curta + link "Ver log" |
| Admin não disponível | Banner fixo no topo: "Executando sem admin — funcionalidades limitadas" |

---

## Linguagem e Tom

**Fazer:**
- "42ms — Ótimo para jogos"
- "Packet loss: 0.2% — Conexão estável"
- "DNS aplicado. Velocidade de resolução melhorou 74%"
- "CS2 detectado — prioridade Alta aplicada automaticamente"

**Evitar:**
- "Seu ping caiu 500%!" (inflado)
- "Otimizando com tecnologia avançada..." (vago)
- "Conexão turbo ativada" (enganoso)
- Jargão técnico sem explicação no tooltip

---

## Estados de Loading e Erro

| Estado | Visual |
|---|---|
| Diagnóstico rodando | Spinner inline + texto "Testando Valve (CS2)..." |
| Sem admin | Banner âmbar no topo + ícones 🔒 nas ações bloqueadas |
| Sem internet | Card de erro: "Sem conexão detectada. Verifique sua rede." |
| Licença inválida | LicenseWindow sobreposta (reusar padrão WinBoost) |
| Ação com restart | Modal de confirmação: "Esta ação requer reinício. Deseja agendar?" |
