# MatchKS - Guia Completo

Este plugin organiza partidas de CS2 com foco em campeonato/pug com:

- fluxo de aquecimento -> ready -> live
- round de faca opcional com escolha de lado
- backups de round e restore
- demos GOTV
- pausa tática/técnica
- nomes de times com persistência por SteamID
- relatório de fim de mapa (arquivo local + webhook Discord)

## Visao Geral Do Fluxo

1. Plugin carrega e cria pastas/configs.
2. Executa `warmup.cfg` no inicio do mapa.
3. Times definem nome e jogadores dão `!ready`.
4. Quando ambos os lados estão prontos, inicia match:
- se round de faca estiver ativo: joga faca, vencedor escolhe `.stay` ou `.switch`
- se não: vai direto para live
5. Match live roda com backup automatico por round.
6. No fim do mapa:
- salva arquivo de stats por jogador
- envia stats para Discord webhook
- grava resumo de partida
- vai para proximo mapa (se serie) ou encerra tudo

## Arquivos Gerados

- `active_match.json` (estado da serie/mapa atual)
- `csgo/BackupMatchKS/*.txt` (backups de round)
- `csgo/cfg/MatchKS/team_name_owner.json` (SteamID -> nome do time)
- `csgo/cfg/MatchKS/history/match_end_*.txt` (resumo final)
- `csgo/cfg/MatchKS/history/map_end_stats_*.txt` (stats por mapa)
- `csgo/<demo_pasta>/*.dem` (demos GOTV)

## Configuracao (config.cfg)

Arquivo: `csgo/cfg/MatchKS/config.cfg`

Campos principais:

- `PausesTaticoPorEquipe`
- `DuracaoPauseTatico`
- `RoundFaca`
- `FogoAmigo`
- `ChatPrefixText`
- `ChatPrefixColor`
- `nome_formato_demo`
- `demo_pasta`
- `discord_webhook_enabled`
- `discord_webhook_url`

## Comandos

### Admin

- `.map <mapa>`: troca mapa
- `.kill`: mata o time inimigo do admin
- `.slap <numero> [tapas]`: slap em jogador da lista
- `.time1 <nome>`: define nome do lado CT
- `.time2 <nome>`: define nome do lado TR
- `.config`: mostra configuracao da partida
- `.start`: força inicio (marca os dois times como ready)
- `.restart`: volta para aquecimento
- `.adm <mensagem>`: chat admin global
- `.rk`: ativa/desativa round de faca no mapa atual
- `.skinsperso`: ativa/desativa verificador de skin
- `.nobots`: remove bots e evita autojoin
- `.bots`: completa servidor com bots
- `.tec` ou `.tech`: pausa tecnica (toggle)
- `.backups`: lista backups disponiveis
- `.restore <round>`: restaura backup de round

### Jogador

- `.nometime <nome>`: define nome do time do lado atual
- `.r`, `.ready`, `.pronto`: marca ready do time
- `.rrlive`: confirma retomada apos restore (precisa 1 confirmacao de cada time)
- `.stay` ou `.ficar`: manter lado apos faca
- `.switch` ou `.trocar`: trocar lado apos faca
- `.tac` ou `.timeout`: pausa tatica

## Nomes De Times E SteamID

Quando um jogador usa `.nometime`, o plugin salva:

- SteamID do jogador
- nome do time escolhido

Se esse jogador mudar de lado, o nome acompanha ele para o novo time.
Se ele sair daquele lado, o lado antigo volta para o nome default (`Terroristas`/`Contra-Terroristas`) quando aplicavel.

Persistencia fica em `team_name_owner.json`.

## Backup E Restore

O plugin cria backup no inicio de cada round (snapshot de inicio).

- Nome do backup e deterministico por estado de round/placar
- Evita gerar arquivo novo desnecessario em replay apos `restore`
- Fluxo de restore mais proximo do MatchZy: apos `.restore <round>`, a partida entra em pausa de confirmacao

Comandos:

- `.backups`
- `.restore <round>`
- `.rrlive` (jogadores)
- `.ready` (admin override para liberar sem esperar ambos os times)

### Fluxo Pos-Restore

1. Admin usa `.restore <round>`.
2. Plugin carrega o backup e pausa a partida automaticamente (`mp_pause_match`).
3. Um jogador de cada lado usa `.rrlive`.
4. Quando TR e CT confirmam, plugin libera com `mp_unpause_match`.
5. Se necessario, admin pode liberar direto usando `.ready`.

## Demo GOTV

Gravacao inicia no primeiro round live (pos-faca, quando houver).

Configuravel por:

- `nome_formato_demo`
- `demo_pasta`

## Stats De Fim De Mapa

No `EventGameEnd`, se a partida estiver live, o plugin gera:

- arquivo local com lineup e stats
- envio para Discord webhook

### Campos por jogador

- `Kills`
- `Deaths`
- `K/D`
- `K/R` (kills por round)
- `ADR` (dano acumulado por mapa / rounds)

## Discord Webhook

Ative/desative com:

- `discord_webhook_enabled=true|false`

Webhook usado:

- `discord_webhook_url="..."`

No fim de cada mapa o plugin envia uma ou mais mensagens (chunked) com tabela de stats.

## Observacoes Tecnicas

- `kills/deaths` sao lidos com fallback por reflection em multiplos caminhos da API para manter compatibilidade.
- `ADR` vem do dano acumulado no `OnPlayerHurt` durante o mapa.
- Em caso de falha no webhook, o plugin loga warning e segue sem quebrar o fluxo da partida.
