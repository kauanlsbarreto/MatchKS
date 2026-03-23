# MatchKS

Plugin de CS2 para organizar partidas de campeonato e PUG. Desenvolvido com CounterStrikeSharp v1.0.6+, .NET 8.

---

## Fluxo da Partida

```
Mapa carrega
    └─> warmup.cfg executado
        └─> Times definem nome (.nometime) e dão .ready (5 jogadores cada)
            └─> [Round de faca ativado?]
                ├─ SIM: knife.cfg → faca → vencedor escolhe .stay ou .switch → live.cfg → AO VIVO
                └─ NAO: live.cfg → AO VIVO
                    └─> Gravação de demo inicia no 1º round
                        └─> Fim do mapa → stats salvas → webhook Discord → próximo mapa (série) ou encerramento
```

Antes da partida iniciar, o chat exibe a cada 5 segundos os comandos disponíveis, configurações ativas (fogo amigo, faca, overtime) e limites de pausa — para quando a partida vai ao vivo.

---

## Arquivos Gerados pelo Plugin

| Arquivo | Localização | Descrição |
|---|---|---|
| `active_match.json` | `csgo/` (raiz do servidor) | Estado da série/mapa em curso |
| `config.cfg` | `csgo/cfg/MatchKS/` | Configurações do plugin |
| `team_name_owner.json` | `csgo/cfg/MatchKS/` | Persistência SteamID → nome do time |
| `history/match_end_*.txt` | `csgo/cfg/MatchKS/history/` | Resumo de fim de partida |
| `history/map_end_stats_*.txt` | `csgo/cfg/MatchKS/history/` | Stats por jogador por mapa |
| `*.dem` | `csgo/<demo_pasta>/` | Demo GOTV gravada |
| `round_*.cfg` | `csgo/BackupMatchKS/<jogo>/` | Backups de round para restore |
| `session_info.json` | `csgo/BackupMatchKS/<jogo>/` | Dados de sessão para crash recovery |

### CFGs executados pelo plugin

| Arquivo | Quando é executado |
|---|---|
| `csgo/cfg/MatchKS/warmup.cfg` | Início do mapa e `.restart` |
| `csgo/cfg/MatchKS/knife.cfg` | Início do round de faca |
| `csgo/cfg/MatchKS/live.cfg` | Quando a partida vai ao vivo |
| `csgo/cfg/MatchKS/gotv.cfg` | ~5s após o mapa carregar |

Os arquivos `warmup.cfg`, `knife.cfg` e `live.cfg` são **criados automaticamente** na primeira inicialização caso não existam. O `live.cfg` tem `mp_friendlyfire`, `mp_overtime_enable` e `mp_overtime_startmoney` sincronizados a cada partida com os valores do `config.cfg`.

---

## Configuração (config.cfg)

Localização: `csgo/cfg/MatchKS/config.cfg`

```
PausesTaticoPorEquipe=4
DuracaoPauseTatico=30
RoundFaca=true
FogoAmigo=false
EnableOvertime=true
OvertimeStartMoney=10000
ChatPrefixText="MatchKS"
ChatPrefixColor="blue"
nome_formato_demo="{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}"
demo_pasta="matchksDEMOS/"
discord_webhook_enabled=true
discord_webhook_url="https://..."
```

| Campo | Tipo | Padrão | Descrição |
|---|---|---|---|
| `PausesTaticoPorEquipe` | int | `4` | Número de pausas táticas por time por mapa |
| `DuracaoPauseTatico` | int | `30` | Duração em segundos de cada pausa tática |
| `RoundFaca` | bool | `true` | Habilita round de faca por padrão |
| `FogoAmigo` | bool | `false` | Habilita fogo amigo na partida ao vivo |
| `EnableOvertime` | bool | `true` | Habilita overtime na partida ao vivo |
| `OvertimeStartMoney` | int | `10000` | Dinheiro inicial em cada halftime de overtime (`mp_overtime_startmoney`) |
| `ChatPrefixText` | string | `MatchKS` | Texto do prefixo no chat |
| `ChatPrefixColor` | string | `blue` | Cor do prefixo (blue, red, green, gold, orange, purple, lime, lightblue, default) |
| `nome_formato_demo` | string | `{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}` | Formato do nome da demo gravada |
| `demo_pasta` | string | `matchksDEMOS/` | Pasta de destino das demos (relativa a `csgo/`) |
| `discord_webhook_enabled` | bool | `true` | Ativa/desativa envio de stats para Discord |
| `discord_webhook_url` | string | — | URL do webhook do Discord |

---

## Comandos

### Admin (requer `@css/kick`)

| Comando | Alias | Descrição |
|---|---|---|
| `.map <mapa>` | | Troca o mapa |
| `.kill` | | Elimina todos os jogadores do time inimigo do admin |
| `.slap <numero> [tapas]` | | Aplica slap em jogador da lista numerada |
| `.time1 <nome>` | | Define nome do time CT |
| `.time2 <nome>` | | Define nome do time TR |
| `.start` | `.comecar` | Força início da partida (marca os dois times como prontos) |
| `.restart` | `.recomecar` | Volta para aquecimento, resetando o estado |
| `.adm <mensagem>` | | Envia mensagem global como admin |
| `.backups` | | Lista backups disponíveis para o jogo/mapa atual |
| `.restore <round>` | | Restaura backup de round (somente com partida ao vivo) |
| `.tec` | `.tech` | Pausa técnica toggle (sem limite de tempo) |
| `.nobots` | | Remove bots e desativa autojoin |
| `.bots` | | Completa os times com bots |
| `.skinsperso` | | Ativa/desativa verificador de skin personalizada |

### Admin (requer `@css/root`)

| Comando | Descrição |
|---|---|
| `.rk` | Ativa/desativa round de faca para o mapa atual |

### Qualquer jogador

| Comando | Alias | Descrição |
|---|---|---|
| `.config` | | Exibe configuração atual da partida |
| `.r` | `.ready` `.pronto` | Marca o time como pronto (exige 5 jogadores no time) |
| `.nometime <nome>` | | Define o nome do time do seu lado (somente fora do live) |
| `.stay` | `.ficar` | Mantém o lado após o round de faca |
| `.switch` | `.trocar` | Troca de lado após o round de faca |
| `.tac` | `.timeout` | Solicita pausa tática (somente em live) |
| `.readyrr` | | Confirma prontidão após um restore de round |

---

## Pausa Tática

- Cada time tem `PausesTaticoPorEquipe` pausas por mapa
- Duração máxima: ` ` segundos — ao expirar, despausa automaticamente
- Pode ser solicitada durante o freezetime ou agendada para o próximo round
- Durante a pausa, um HUD exibe o time e o tempo restante para todos os jogadores
- Não é possível solicitar pausa se já houver uma em andamento (tática ou técnica) ou agendada

## Pausa Técnica

- Exclusiva para admins (`.tec`)
- Sem limite de tempo — despausa ao usar `.tec` novamente
- Exibe HUD contínuo para todos os jogadores enquanto pausado

---

## Round de Faca

Quando ativado (via `RoundFaca=true` no config ou por mapa via `EnableKnifeRound` no `active_match.json`):

1. Executa `knife.cfg`, zera dinheiro e armas de todos os times
2. Vencedor é determinado por: eliminação total → HP total restante → jogadores vivos → critério do jogo
3. Vencedor tem 60 segundos para escolher `.stay` ou `.switch`
4. Se o tempo expirar, o plugin mantém o lado original automaticamente
5. Após a escolha, executa `live.cfg` e inicia a partida

O admin pode desativar/ativar o round de faca para o mapa atual com `.rk` (requer root).

---

## Nomes de Times e Persistência por SteamID

- O primeiro jogador a entrar em cada time recebe o nome `time_<NomeDoJogador>` automaticamente
- Qualquer jogador pode usar `.nometime <nome>` para definir o nome do seu time (antes do live)
- Admins podem usar `.time1` (CT) e `.time2` (TR) a qualquer momento antes do live
- O SteamID do dono do nome é salvo em `team_name_owner.json`
- Se o proprietário trocar de time, o nome acompanha ele para o novo lado
- Se ele sair do lado, o nome antigo é resetado para o padrão (`Terroristas` / `Contra-Terroristas`)

---

## Backup e Restore de Rounds

A cada round ao vivo (exceto faca e escolha de lado), o plugin executa `mp_backup_round_file` e salva:

```
csgo/BackupMatchKS/<m1_mapa_Time1_vs_Time2>/round_01_20260323_123456.cfg
csgo/BackupMatchKS/<m1_mapa_Time1_vs_Time2>/session_info.json
```

### Comandos de backup

- `.backups` — lista todos os rounds disponíveis para o jogo atual. Se não houver backup para o jogo atual, faz fallback para a pasta de backup mais recente em `BackupMatchKS/`
- `.restore <round>` — restaura o round indicado. **Só funciona com a partida ao vivo.** Após o restore:
  1. Partida é pausada automaticamente
  2. HUD exibe status de confirmação dos dois times
  3. Cada time usa `.readyrr` para confirmar prontidão
  4. Quando ambos confirmam, a partida é despausada

---

## Crash Recovery (Detecção Automática)

20 segundos após o mapa iniciar, o plugin verifica se há uma sessão anterior não encerrada normalmente (crash do servidor):

- Varre todas as pastas em `csgo/BackupMatchKS/`
- Compara os SteamIDs da sessão com os jogadores conectados (threshold: 70% de reconexão)
- Se detectado, avisa no chat com nome dos times, mapa e último round disponível:

```
[CRASH RECOVERY] Partida anterior detectada: TeamA vs TeamB | Mapa: de_inferno
Round 8 disponível (9/10 jogadores reconectados).
Dê .ready nos dois times. Após partir ficará ao vivo, o admin usa .restore 8 para restaurar.
```

Sessões são marcadas como `MatchEnded=true` ao fim normal da partida para não serem re-sugeridas.

---

## Demo GOTV

- A gravação inicia no primeiro round ao vivo (após faca, se houver)
- O nome do arquivo segue o formato `nome_formato_demo` configurado
- A demo é salva em `csgo/<demo_pasta>/`
- A gravação para automaticamente ao fim do mapa

Variáveis disponíveis no formato:

| Variável | Valor |
|---|---|
| `{TIME}` | Data/hora no formato `yyyy-MM-dd_HH-mm` |
| `{MATCH_ID}` | Timestamp em ticks (único por partida) |
| `{MAP}` | Nome do mapa atual |
| `{TEAM1}` | Nome do time TR |
| `{TEAM2}` | Nome do time CT |

---

## Stats de Fim de Mapa

Ao fim de cada mapa com partida ao vivo, o plugin gera um arquivo local e envia para o webhook Discord.

### Campos por jogador

| Campo | Descrição |
|---|---|
| `Kills` | Total de abates |
| `Deaths` | Total de mortes |
| `K/D` | Razão kills/deaths |
| `K/R` | Kills por round |
| `ADR` | Dano médio por round (acumulado via `EventPlayerHurt`) |

O envio ao Discord é feito em chunks caso a tabela ultrapasse 2000 caracteres. Falhas no webhook são logadas sem interromper o fluxo da partida.

---

## Verificador de Skin

Quando ativado pelo admin (`.skinsperso`), o plugin verifica no spawn de cada jogador se o modelo utilizado está na whitelist de modelos padrão do CS2. Skins de terceiros são detectadas e o jogador é notificado.

---

## Relatório de Dano por Round

Ao fim de cada round ao vivo (exceto faca), cada jogador recebe no chat um relatório de dano mostrando quanto causou em cada inimigo e o HP restante deles.

---

## Sincronização de tv_delay

Ao carregar o plugin, o valor de `tv_delay` definido em `gotv.cfg` é automaticamente replicado para todos os outros arquivos `.cfg` dentro de `csgo/cfg/` que contenham essa linha. Mantém a consistência do delay da GOTV entre diferentes configs.
