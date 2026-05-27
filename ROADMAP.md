# AYVU NoLag — Roadmap

## v0.1.0 — MVP Local ✅ em desenvolvimento
- Diagnóstico de ping, jitter, packet loss
- Teste de DNS
- Detecção de processos de jogos
- Priorização de processo
- Limpeza de memória
- Recomendações reversíveis

---

## v0.2.0 — Perfis e Histórico
- Perfis por jogo salvos (processo + configurações)
- Histórico de sessões de diagnóstico
- Comparativo antes/depois por sessão
- Export de relatório como HTML

---

## v0.3.0 — Otimizações de Rede Locais
- Desativar algoritmo de Nagle por adaptador (registro)
- Remover reserva QoS 20% de largura de banda
- Plano de energia Alto Desempenho / Ultimate
- Limpeza completa de RAM (working sets + standby list)
- Flush DNS automático ao iniciar sessão de jogo

---

## v0.4.0 — DNS Optimizer
- Benchmark automático entre Cloudflare, Google, OpenDNS, Quad9 e ISP
- Aplicar DNS mais rápido com 1 clique
- Reversão para DNS original garantida
- Exibir impacto mensurado na latência de resolução

---

## v1.0.0 — Produto Completo Local
- Todos os módulos acima integrados
- Dashboard em tempo real (ping, jitter, RAM, CPU)
- Gráfico ao vivo dos últimos 60s
- Sistema de licença Trial + Definitivo (Firebase)
- UX final conforme `UX.md`
- Build Release + pacote em `08-packages-zips/`

---

## v1.5.0 — Agente e Overlay
- Agente leve em segundo plano (detecta início de jogo)
- Aplicação automática de perfil ao detectar jogo
- Overlay minimalista durante partida (ping, FPS estimado)
- Histórico persistente com ranking de performance por jogo

---

## v2.0.0 — Infraestrutura AYVU (requer servidores)

> ⚠️ Esta versão exige investimento em infraestrutura.

**Pré-requisitos:**
- Servidores de borda em regiões BR/SA/US
- Domínio e certificados para endpoints AYVU
- Sistema de billing de banda por usuário
- Equipe de operações de rede (ou parceria)

**Funcionalidades:**
- Túnel AYVU: roteamento do tráfego de jogo via servidores AYVU
- Seleção automática do servidor com menor latência
- Protocolo de tunelamento: WireGuard ou similar (sem driver pesado)
- Painel de status dos servidores AYVU em tempo real

---

## v3.0.0 — Multipath / Multi-Internet (requer driver de rede)

> ⚠️ Esta versão é equivalente ao NoPing/ExitLag em escopo técnico.

**Pré-requisitos:**
- Driver de rede Windows (NDIS ou equivalente) — alto custo de desenvolvimento
- Suporte técnico de rede especializado
- Infraestrutura de servidores distribuída globalmente
- Equipe de segurança (driver = superfície de ataque)

**Funcionalidades:**
- Multipath: usar múltiplas conexões de internet em paralelo
- Failover automático entre conexões
- Roteamento inteligente por latência e disponibilidade
- Equivalente técnico ao ExitLag Multipath

---

## Notas para Decisão de Investimento

### Por que não começar com o túnel?

1. **Custo de infraestrutura**: servidores de borda custam R$3k-15k/mês dependendo do tráfego
2. **Complexidade técnica**: driver de rede Windows é um dos softwares mais complexos do ecossistema
3. **Risco de produto**: sem provar o produto localmente, difícil justificar o investimento
4. **Vantagem competitiva local**: diagnóstico honesto + otimização local é diferenciação real vs NoPing/ExitLag que focam só no túnel

### Caminho recomendado

```
v0.1 → v1.0 (local, licença única) 
  → provar mercado e base de usuários
  → v1.5 (histórico, overlay) 
  → avaliar receita e demanda por túnel
  → v2.0 (túnel, infraestrutura) com receita recorrente sustentando os servidores
  → v3.0 (multipath) apenas se mercado justificar
```
