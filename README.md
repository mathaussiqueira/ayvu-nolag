# AYVU NoLag

AYVU NoLag e um otimizador local para jogos online em Windows.

Esta primeira versao nao tenta ser um clone completo de NoPing ou ExitLag. O MVP entrega diagnostico, leitura de estabilidade e acoes locais seguras para reduzir atrito antes de uma partida.

## Escopo v0.1.0

- App WPF em .NET 8.
- Diagnostico de ping para alvos externos.
- Calculo de ping medio, melhor ping, jitter aproximado e packet loss.
- Teste de resolucao DNS para hosts conhecidos.
- Deteccao de processos provaveis de jogos.
- Boost local com priorizacao do processo selecionado.
- Limpeza leve da memoria de trabalho do proprio app.
- Recomendacoes simples e reversiveis.

## Fora do escopo desta versao

- Tunel de rede proprio.
- Multipath real.
- Driver de rede.
- Troca automatica de DNS.
- Modificacao permanente de registro, firewall ou adaptadores.
- Promessa de reduzir ping quando o problema e distancia fisica ate o servidor.

## Estrutura

```txt
05-services/ayvu-nolag/
  app/
    AYVUNoLag.csproj
    MainWindow.xaml
    MainWindow.xaml.cs
    Models/
    Services/
  README.md
  VERSION.md
```

## Como validar

```powershell
dotnet build .\05-services\ayvu-nolag\app\AYVUNoLag.csproj -c Release
```

## Caminho do build

```txt
05-services/ayvu-nolag/app/bin/Release/net8.0-windows/
```

## Proximas frentes

- Claude: definir proposta comercial, UX final, textos e roadmap de produto.
- Codex: adicionar historico de diagnosticos, perfis por jogo e export de relatorio.
- Futuro: estudar tunel com servidor proprio, medicao de rota e infraestrutura multi-regiao.
