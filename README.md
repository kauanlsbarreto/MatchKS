# MatchKS — Documentação Completa

Plugin de CS2 para organizar partidas competitivas (PUG e campeonato), desenvolvido com **CounterStrikeSharp v1.0.6+** e **.NET 8**.

---

## Sumário

1. [Visão Geral](#1-visão-geral)
2. [Instalação e Estrutura de Arquivos](#2-instalação-e-estrutura-de-arquivos)
3. [Configuração (config.cfg)](#3-configuração-configcfg)
4. [Fluxo Completo de uma Partida](#4-fluxo-completo-de-uma-partida)
   - 4.1 [Mapa carrega — Warmup](#41-mapa-carrega--warmup)
   - 4.2 [Nomes de Times](#42-nomes-de-times)
   - 4.3 [Prontidão (Ready)](#43-prontidão-ready)
   - 4.4 [Round de Faca](#44-round-de-faca)
   - 4.5 [Escolha de Lado](#45-escolha-de-lado)
   - 4.6 [Partida Ao Vivo](#46-partida-ao-vivo)
   - 4.7 [Intervalo (Halftime)](#47-intervalo-halftime)
   - 4.8 [Fim de Mapa — Próximo Mapa ou Encerramento](#48-fim-de-mapa--próximo-mapa-ou-encerramento)
5. [Comandos — Jogadores](#5-comandos--jogadores)
6. [Comandos — Admin (@css/kick)](#6-comandos--admin-csskick)
7. [Comandos — Super Admin (@css/root)](#7-comandos--super-admin-cssroot)
8. [Pausa Tática](#8-pausa-tática)
9. [Pausa Técnica](#9-pausa-técnica)
10. [Backup e Restore de Rounds](#10-backup-e-restore-de-rounds)
11. [Crash Recovery](#11-crash-recovery)
12. [Demo GOTV](#12-demo-gotv)
13. [Relatório de Dano por Round](#13-relatório-de-dano-por-round)
14. [Stats de Fim de Mapa e Discord](#14-stats-de-fim-de-mapa-e-discord)
15. [Votação de Troca de Mapa (!mudarmapa)](#15-votação-de-troca-de-mapa-mudarmapa)
16. [Verificador de Skin](#16-verificador-de-skin)
17. [Sincronização de tv_delay](#17-sincronização-de-tv_delay)
18. [Nomes de Times e Persistência por SteamID](#18-nomes-de-times-e-persistência-por-steamid)

---

## 1. Visão Geral

O MatchKS automatiza completamente o ciclo de vida de uma partida competitiva no CS2:

- **Warmup** automático ao carregar o mapa
- Anúncio periódico de comandos e status de prontidão no chat
- **Round de faca** opcional antes de ir ao vivo
- **Escolha de lado** pelo time vencedor do round de faca
- **Partida ao vivo** com backups de round a cada round
- **Pausas táticas e técnicas** com HUD dedicado
- **Stats por jogador** ao fim de cada mapa
- Envio de stats para **webhook Discord**
- **Gravação de demo** GOTV automática
- **Crash Recovery** — detecta partidas interrompidas e sugere restore
- **Votação de troca de mapa** por qualquer jogador durante o warmup
- **Verificador de skin** para garantir modelos padrão

---

## 2. Instalação e Estrutura de Arquivos

Copie `MatchKS.dll` para `csgo/addons/counterstrikesharp/plugins/MatchKS/`.

Na primeira execução, o plugin cria automaticamente:

| Arquivo / Pasta | Localização | Descrição |
|---|---|---|
| `config.cfg` | `csgo/cfg/MatchKS/` | Configurações do plugin |
| `warmup.cfg` | `csgo/cfg/MatchKS/` | Executado no warmup |
| `knife.cfg` | `csgo/cfg/MatchKS/` | Executado no round de faca |
| `live.cfg` | `csgo/cfg/MatchKS/` | Executado ao ir ao vivo |
| `gotv.cfg` | `csgo/cfg/MatchKS/` | Configurações GOTV (deve existir) |
| `active_match.json` | `csgo/` (raiz) | Estado da série em andamento |
| `history/match_end_*.txt` | `csgo/cfg/MatchKS/history/` | Resumo de fim de partida |
| `history/map_end_stats_*.txt` | `csgo/cfg/MatchKS/history/` | Stats por jogador por mapa |
| `*.dem` | `csgo/<demo_pasta>/` | Demo GOTV gravada |
| `round_*.cfg` | `csgo/BackupMatchKS/<jogo>/` | Backups de round |
| `session_info.json` | `csgo/BackupMatchKS/<jogo>/` | Dados de sessão para crash recovery |

---

## 3. Configuração (config.cfg)

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
| `PausesTaticoPorEquipe` | int | `4` | Pausas táticas por time por mapa |
| `DuracaoPauseTatico` | int | `30` | Segundos de cada pausa tática |
| `RoundFaca` | bool | `true` | Habilita round de faca por padrão |
| `FogoAmigo` | bool | `false` | Fogo amigo na partida ao vivo |
| `EnableOvertime` | bool | `true` | Habilita overtime |
| `OvertimeStartMoney` | int | `10000` | Dinheiro inicial em cada half do overtime |
| `ChatPrefixText` | string | `MatchKS` | Texto do prefixo no chat |
| `ChatPrefixColor` | string | `blue` | Cor do prefixo (`blue`, `red`, `green`, `gold`, `orange`, `purple`, `lime`, `lightblue`, `default`) |
| `nome_formato_demo` | string | veja padrão | Formato do nome do arquivo de demo |
| `demo_pasta` | string | `matchksDEMOS/` | Pasta de destino das demos (relativa a `csgo/`) |
| `discord_webhook_enabled` | bool | `true` | Ativa envio de stats para Discord |
| `discord_webhook_url` | string | — | URL do webhook Discord |

---

## 4. Fluxo Completo de uma Partida

```
Mapa carrega (OnMapStart)
  └─> warmup.cfg executado
  └─> Anúncios periódicos de comandos e status a cada 15s (chat)
  └─> Verificação do modo competitivo a cada 15s
  └─> 20s após: Crash Recovery verifica partidas anteriores
      │
      ├─> Jogadores definem nomes de time (.nometime)
      ├─> Jogadores dão .ready (5 por time exigido)
      │
      └─> AMBOS OS TIMES PRONTOS?
          │
          ├─ Round de Faca ATIVADO?
          │   ├─ SIM → knife.cfg → faca → vencedor escolhe .stay ou .switch (60s)
          │   │         └─> live.cfg → AO VIVO
          │   └─ NÃO → live.cfg → AO VIVO
          │
          └─> AO VIVO
              ├─> mp_restartgame 1 (1s após)
              ├─> Demo inicia no 1º round
              ├─> Backup de round criado a cada round
              ├─> Relatório de dano exibido ao fim de cada round
              ├─> Round 12: troca automática de lado (halftime)
              └─> Fim de mapa (EventGameEnd)
                  ├─> Stats salvos localmente e enviados ao Discord
                  ├─> Demo parada
                  ├─> Série com mais mapas? → próximo mapa em 15s
                  └─> Série encerrada? → resumo final, reset ao warmup
```

### 4.1 Mapa carrega — Warmup

Ao carregar qualquer mapa, o plugin:

1. Executa `exec MatchKS/warmup.cfg` (1 segundo após o carregamento)
2. Executa `exec MatchKS/gotv.cfg` (5 segundos após o carregamento)
3. Inicia timers periódicos:
   - A cada **15s**: anuncia quais times ainda não deram pronto e quantos jogadores faltam
   - A cada **15s**: verifica se o servidor está em modo competitivo (`game_type 0 / game_mode 1`) e avisa no chat se não estiver
   - A cada **5s**: exibe comandos disponíveis no chat (para enquanto não for ao vivo)
4. Se o `active_match.json` existir na raiz do servidor (de uma série anterior), carrega o estado da série automaticamente
5. Após **20s**: executa a verificação de Crash Recovery

O arquivo `warmup.cfg` padrão criado pelo plugin configura:
```
mp_warmuptime 999       // warmup "infinito" até os dois times darem pronto
mp_buy_anywhere 1
mp_buytime 9999
mp_autoteambalance 0
mp_limitteams 0
mp_friendlyfire 0
mp_freezetime 6
```

---

### 4.2 Nomes de Times

- Quando o **primeiro jogador humano** entra em um time durante o warmup, o plugin define automaticamente `time_<NomeDoJogador>` como nome padrão daquele time
- Qualquer jogador pode alterar o nome do **próprio** time com `.nometime <nome>` (apenas durante o warmup)
- Admins podem definir nomes com `.time1 <nome>` (CT) e `.time2 <nome>` (TR) a qualquer momento antes do ao vivo

---

### 4.3 Prontidão (Ready)

Comando: `.ready` / `.r` / `.pronto`

- Qualquer jogador humano pode acionar o ready do **seu time**
- O time precisa ter **pelo menos 5 jogadores humanos** para poder dar pronto
- Quando ambos os times estão prontos, o plugin verifica se os nomes estão definidos e inicia o processo

> **Observação**: Admins podem forçar o início mesmo sem 5 jogadores usando `.start`.

**O que o plugin checa antes de iniciar:**
1. `_activeMatch` não é nulo
2. Nome do Team1 não está vazio
3. Nome do Team2 não está vazio
4. `_isTeamTReady == true`
5. `_isTeamCTReady == true`

Se algum ponto falhar, o plugin informa no chat o que está faltando.

---

### 4.4 Round de Faca

Ativado por padrão (`RoundFaca=true` no config). Pode ser desativado por mapa via `active_match.json` (`EnableKnifeRound: false`), ou alterado em tempo real pelo admin com `.rk`.

**Sequência:**

1. `exec MatchKS/knife.cfg` é executado
2. Dinheiro e armas são zerados via cvar (`mp_startmoney 0`, `mp_maxmoney 0`, armas default limpas)
3. `mp_restartgame 1` após 1s
4. Jogadores jogam apenas com faca
5. Ao término do round, o vencedor é determinado por este critério (em ordem de prioridade):
   - **Eliminação total** do time adversário
   - **Maior HP total** dos jogadores vivos
   - **Maior número de jogadores vivos**
   - **Critério padrão do jogo** (empate)
6. Se `CsTeam.None` for retornado (impossível determinar), a fase de escolha de lado **não** é iniciada para evitar estado inválido

O arquivo `knife.cfg` padrão criado pelo plugin:
```
mp_maxrounds 2
mp_roundtime 2
mp_roundtime_defuse 2
mp_startmoney 0
mp_maxmoney 0
mp_buy_anywhere 0
mp_friendlyfire 0
mp_freezetime 6
```

---

### 4.5 Escolha de Lado

Após o round de faca, a partida é **pausada** e o time vencedor tem **60 segundos** para escolher.

- `.stay` / `.ficar` — mantém o lado atual
- `.switch` / `.trocar` — troca de lado

**Apenas o time vencedor do round de faca** pode usar esses comandos. Se o tempo espirar sem escolha, o plugin mantém o lado original automaticamente.

Um HUD com contador regressivo é exibido para todos:
```
ESCOLHA DE LADO
.stay ou .switch
[60s]
```

Após a escolha:
- Se `.switch`: `mp_swapteams` é executado e o estado interno dos times é trocado (incluindo pausas usadas)
- `mp_unpause_match` é chamado
- Nomes de time são reaplicados via `UpdateTeamNames()`
- `StartMatch()` é chamado

---

### 4.6 Partida Ao Vivo

`StartMatch()` executa a seguinte sequência:

1. `_isMatchLive = true`, `_isTeamChangeLocked = true`
2. Sincroniza `live.cfg` com os valores atuais do `config.cfg` (`FogoAmigo`, `EnableOvertime`, `OvertimeStartMoney`)
3. `exec MatchKS/live.cfg`
4. Aplica cvars individualmente: `mp_friendlyfire`, `mp_overtime_enable`, `mp_overtime_startmoney`
5. Anuncia no chat: **"A partida está AO VIVO!"**
6. `mp_restartgame 1` após 1s
7. Flag `_isDemoStartPending = true` — a demo inicia no primeiro `EventRoundStart`

O arquivo `live.cfg` padrão criado pelo plugin:
```
mp_maxrounds 24
mp_overtime_maxrounds 6
mp_autoteambalance 0
mp_limitteams 0
mp_buytime 20
mp_buy_anywhere 0
mp_freezetime 15
mp_roundtime 1.92
mp_roundtime_defuse 1.92
```

Ao longo da partida ao vivo, a cada round (`EventRoundStart`):
- Demo inicia se `_isDemoStartPending` estiver ativo
- Backup do round é criado (ver [seção 10](#10-backup-e-restore-de-rounds))
- Pausas agendadas são iniciadas (tática ou técnica)

Ao fim de cada round (`EventRoundEnd`):
- Relatório de dano é exibido no chat (ver [seção 13](#13-relatório-de-dano-por-round))
- Verifica halftime (round 12)

---

### 4.7 Intervalo (Halftime)

No round 12 (ao fim do `EventRoundEnd`), o plugin:

1. Chama `ApplyTrackedSideSwap()`: troca internamente `Team1 ↔ Team2` e todos os estados associados (pausas usadas, time da pausa, etc.)
2. Abre uma janela de **12 segundos** (`_allowAutoSideSwapWindow = true`) durante a qual um eventual evento `EventCsWinPanelRound` aplica a troca definitiva
3. Reaaplica os nomes de time via `UpdateTeamNames()`

> Isso garante que o placar interno e os nomes de time estejam sempre corretos após o intervalo, independente do comportamento do motor.

---

### 4.8 Fim de Mapa — Próximo Mapa ou Encerramento

Ao receber `EventGameEnd`, o plugin chama `HandleMatchEnd()`:

**Proteção contra dupla execução:** `_isHandlingMatchEnd` garante que a lógica rode apenas uma vez por mapa.

**Se a partida não estava ao vivo** (ex.: mapa encerrado durante warmup):
- `ResetMatchState()` é chamado
- Plugin volta ao warmup via `ReinitializeWarmupState()`

**Se a partida estava ao vivo:**

1. `FinalizeCurrentMapArtifacts()`:
   - Salva stats do mapa (`WritePerMapPlayerStatsFile()`)
   - Para a gravação de demo (`StopDemoRecording()`)
2. Série com múltiplos mapas (`MapList.Count > 1`)?
   - **SIM** → `GoToNextMap()`: incrementa `CurrentMapIndex`, salva `active_match.json`, `changelevel` em **15 segundos**
   - **NÃO** → `EndFullMatch()`: grava resumo, limpa `active_match.json`, reseta para warmup

`EndFullMatch()` executa:
1. `WriteMatchSummaryFile()` — arquivo `match_end_*.txt` com placar final e contagem de mapas vencidos
2. `MarkSessionEnded()` — marca `session_info.json` com `MatchEnded=true` para evitar falso Crash Recovery
3. `ResetMatchState()` — limpa todos os timers e estados
4. `_activeMatch = null`
5. Deleta `active_match.json`
6. `ReinitializeWarmupState()` — executa warmup.cfg e aguarda novos prontos

---

## 5. Comandos — Jogadores

> Nenhuma permissão especial necessária. Maioria exige `CLIENT_ONLY` (não funciona no console do servidor).

| Comando | Alias | Quando | Descrição |
|---|---|---|---|
| `.r` | `.ready` `.pronto` | Warmup | Marca o time do jogador como pronto. Exige 5 jogadores no time. |
| `.nometime <nome>` | | Warmup | Define o nome do time do próprio lado. |
| `.stay` | `.ficar` | Após faca | Mantém o lado após o round de faca. Apenas o time vencedor pode usar. |
| `.switch` | `.trocar` | Após faca | Troca de lado após o round de faca. Apenas o time vencedor pode usar. |
| `.tac` | `.timeout` | Ao vivo | Solicita pausa tática. Agendada para o próximo round se fora do freezetime. |
| `.tec` | `.tech` | Ao vivo | Pausa técnica toggle — sem limite de tempo, despausado com `.tec` novamente. |
| `.config` | | Qualquer hora | Exibe no chat a configuração atual da partida (modo, faca, overtime, pausas, placares). |
| `.backups` | | Ao vivo | Lista backups de round disponíveis. |
| `.readyrr` | | Após restore | Confirma prontidão após um restore de round (ambos os times precisam confirmar). |
| `.mudarmapa <mapa>` | | Warmup | Inicia votação de troca de mapa. Todos os jogadores nos times precisam aceitar. |
| `.aceitar` | | Durante votação | Aceita a troca de mapa proposta. |
| `.recusar` | | Durante votação | Recusa a troca de mapa e cancela a votação. |
| `.cancelarvoto` | | Durante votação | Cancela uma votação de troca de mapa em andamento. |

---

## 6. Comandos — Admin (@css/kick)

| Comando | Alias | Descrição |
|---|---|---|
| `.map <mapa>` | | Troca o mapa imediatamente via `changelevel`. |
| `.kill` | | Elimina todos os jogadores do time inimigo do admin. |
| `.slap <número> [tapas]` | | Aplica slap em jogador da lista numerada (`.slap` sem argumento lista os jogadores). |
| `.time1 <nome>` | | Define nome do time CT a qualquer momento antes do ao vivo. |
| `.time2 <nome>` | | Define nome do time TR a qualquer momento antes do ao vivo. |
| `.start` | `.comecar` | Força início da partida (marca ambos os times como prontos). |
| `.restart` | `.recomecar` | Volta para warmup, reseta estado e aguarda novos prontos. |
| `.adm <mensagem>` | | Envia mensagem global visível como `[ADM - MatchKS]`. |
| `.backups` | | Lista backups (admin vê no chat; sem jogador conectado, exibe no console). |
| `.restore <round>` | | Restaura backup de round. Só funciona com partida ao vivo. |
| `.tec` | `.tech` | Inicia/desfaz pausa técnica. |
| `.nobots` | | Remove bots e desativa `bot_join_after_player`. |
| `.bots` | | Adiciona bots para completar times até 10 jogadores. |
| `.skinsperso` | | Ativa/desativa verificador de skin não padrão. |

---

## 7. Comandos — Super Admin (@css/root)

| Comando | Descrição |
|---|---|
| `.rk` | Ativa ou desativa o round de faca para o mapa atual (toggle). |

---

## 8. Pausa Tática

- Cada time tem `PausesTaticoPorEquipe` pausas por mapa (padrão: 4)
- Duração máxima: `DuracaoPauseTatico` segundos (padrão: 30s)
- **Durante o freezetime:** pausa começa imediatamente
- **Fora do freezetime:** agendada para o próximo round (pode ser cancelada usando `.tac` novamente antes do round seguinte)
- **Ao terminar o tempo:** `mp_unpause_match` é chamado automaticamente
- **HUD durante a pausa:** exibe o nome do time e o contador regressivo para todos os jogadores

**Restrições:**
- Não é possível solicitar se já houver pausa ativa (tática ou técnica)
- Não é possível solicitar se já houver outra pausa agendada
- Ao ultrapassar o limite, o plugin informa no chat

---

## 9. Pausa Técnica

- Ativada com `.tec` / `.tech` por qualquer jogador (exige `_isMatchLive`)
- Sem limite de tempo — a partida fica pausada indefinidamente
- **Durante o freezetime:** pausa inicia imediatamente
- **Fora do freezetime:** agendada para o próximo round
- Para desbloquear: o mesmo time que pediu usa `.tec` novamente
- **HUD:** exibe "PARTIDA EM PAUSA TÉCNICA" com instrução para despausar, repetido a cada segundo para todos os jogadores

---

## 10. Backup e Restore de Rounds

A cada round ao vivo (exceto round de faca e fase de escolha de lado), o plugin cria:

```
csgo/BackupMatchKS/<chave_do_jogo>/round_01_20260323_123456.cfg
csgo/BackupMatchKS/<chave_do_jogo>/session_info.json
```

A **chave do jogo** segue o padrão: `m<número_do_mapa>_<mapa>_<time1>_vs_<time2>`.

O `session_info.json` contém:
```json
{
  "Team1Name": "Astralis",
  "Team2Name": "NaVi",
  "MapName": "de_inferno",
  "LastBackupRound": 8,
  "MatchEnded": false,
  "Team1Players": { "steamid": "nome" },
  "Team2Players": { "steamid": "nome" }
}
```

### Restore

1. `.backup` lista os rounds disponíveis para o jogo atual (com fallback para o backup mais recente se nenhum for encontrado para o jogo atual)
2. `.restore <round>` — executa `mp_backup_restore_load_file`; após o restore:
   - Partida é pausada automaticamente
   - HUD exibe status de confirmação dos dois times
   - Cada time usa `.readyrr` para confirmar
   - Quando ambos confirmam, `mp_unpause_match` é chamado

---

## 11. Crash Recovery

**20 segundos** após qualquer mapa carregar, o plugin verifica se há partidas anteriores não encerradas normalmente.

**Algoritmo:**
1. Varre todas as pastas em `csgo/BackupMatchKS/` (da mais recente para a mais antiga)
2. Para cada pasta, lê `session_info.json`
3. Ignora sessões com `MatchEnded=true` ou sem rounds salvos
4. Compara os SteamIDs da sessão com os jogadores conectados no momento
5. **Threshold**: pelo menos **70%** dos jogadores da sessão precisam estar reconectados
6. Se atingido: avisa no chat com nome dos times, mapa e número do último round disponível

**Mensagem exibida:**
```
[CRASH RECOVERY] Partida anterior detectada: TeamA vs TeamB | Mapa: de_inferno
Round 8 disponível (9/10 jogadores reconectados).
Dê .ready nos dois times. Após partir ficará ao vivo, o admin usa .restore 8 para restaurar.
```

---

## 12. Demo GOTV

- A gravação **inicia no primeiro `EventRoundStart`** ao vivo (a flag `_isDemoStartPending` é ativada por `StartMatch()`)
- O nome do arquivo segue o formato configurado em `nome_formato_demo`

| Variável | Valor |
|---|---|
| `{TIME}` | Data/hora: `yyyy-MM-dd_HH-mm` |
| `{MATCH_ID}` | Timestamp em ticks (único por partida) |
| `{MAP}` | Nome do mapa atual |
| `{TEAM1}` | Nome do time TR |
| `{TEAM2}` | Nome do time CT |

- A demo é salva em `csgo/<demo_pasta>/`
- A gravação para automaticamente em `FinalizeCurrentMapArtifacts()` (fim de mapa)
- Se `_currentDemoName` estiver preenchido ao gerar o resumo, o caminho da demo é incluído no arquivo `match_end_*.txt`

---

## 13. Relatório de Dano por Round

Ao fim de **cada round ao vivo** (exceto round de faca), cada jogador humano recebe no chat individual:

```
[MatchKS] Relatório de Dano do Round:
 - [0 HP] NomeInimigo1 = [87 HP]
 - [45 HP] NomeInimigo2
```

- Mostra o HP restante de cada inimigo (0 se morreu)
- Mostra o dano causado pelo jogador naquele inimigo naquele round
- O dano acumulado por jogador ao longo de todo o mapa é usado para calcular o **ADR** nas stats finais

---

## 14. Stats de Fim de Mapa e Discord

Ao fim de cada mapa com partida ao vivo, `WritePerMapPlayerStatsFile()` é chamado:

**Arquivo local** `history/map_end_stats_*.txt`:
```
Data=2026-03-23 14:00:00
Mapa=de_inferno
Rounds=24
TeamTR=Astralis
TeamCT=NaVi
Team;Player;ID;Kills;Deaths;K/D;K/R;ADR
Astralis;dev1ce;76561198...;20;10;2.00;0.83;85.50
...
```

**Campos calculados:**

| Campo | Cálculo |
|---|---|
| `K/D` | `Kills / Deaths` (se sem mortes: igual a kills) |
| `K/R` | `Kills / Rounds` |
| `ADR` | `DanoTotal / Rounds` (via `EventPlayerHurt`) |

**Envio ao Discord:**
- Só ocorre se `discord_webhook_enabled=true` e `discord_webhook_url` estiver preenchido
- A tabela é dividida em chunks de até 1700 caracteres para não ultrapassar o limite de 2000 chars do Discord
- Cada chunk é formatado como bloco de código (` ``` `)
- Falhas no envio são **logadas** sem interromper o fluxo da partida

---

## 15. Votação de Troca de Mapa (!mudarmapa)

Disponível **apenas durante o warmup**. Qualquer jogador pode iniciar.

**Fluxo:**

1. Jogador usa `.mudarmapa <nome_do_mapa>`
2. Se não há jogadores nos times: troca imediata (sem votação)
3. Se há jogadores: votação iniciada com 60 segundos
4. HUD exibido para todos os jogadores com:
   - Nome do mapa proposto
   - Contador `aceitos / total`
   - Contador regressivo em segundos
   - Status individual: "✓ Você aceitou" / "!aceitar  !recusar" / "Espectador — sem voto"
5. Para aceitar: `.aceitar`
6. Para recusar: `.recusar` — cancela toda a votação imediatamente
7. Para cancelar: `.cancelarvoto` — qualquer jogador pode cancelar
8. Se todos aceitarem: `changelevel` em 5 segundos
9. Se o tempo esgotar (60s): votação cancelada automaticamente

**Regras:**
- Espectadores **não votam** e **não bloqueiam** a votação
- Se jogadores desconectarem durante a votação e os restantes já tiverem aceitado, a votação é confirmada automaticamente
- O nome do mapa é sanitizado (remove `"`, `;`, `\n`, `\r`) para prevenir injeção de comandos
- Não é possível iniciar nova votação enquanto há uma em andamento

---

## 16. Verificador de Skin

Ativado pelo admin com `.skinsperso` (toggle). Quando ativo:

- Verificado no `EventPlayerSpawn` (0.5s após o spawn)
- Compara o modelo do jogador com **whitelists** de modelos padrão do CS2:

**CT Whitelist:**
`ctm_fbi`, `ctm_gign`, `ctm_gsg9`, `ctm_idf`, `ctm_sas`, `ctm_st6`, `ctm_swat`

**TR Whitelist:**
`tm_anarchist`, `tm_balkan`, `tm_elite_crew`, `tm_leet`, `tm_phoenix`, `tm_pirate`, `tm_professional`, `tm_separatist`

- Se o modelo não estiver na lista: o jogador é informado no chat e **kickado** com mensagem explicativa
- Bots e espectadores são ignorados

---

## 17. Sincronização de tv_delay

Ao carregar o plugin (na função `Load()`), 10 segundos depois é chamado `SyncTvDelayAcrossConfigs()`:

1. Lê o valor de `tv_delay` no arquivo `csgo/cfg/MatchKS/gotv.cfg`
2. Varre **todos** os arquivos `.cfg` dentro de `csgo/cfg/` recursivamente
3. Em cada arquivo que contenha a linha `tv_delay ...`, substitui pelo valor lido do `gotv.cfg`
4. Loga quantos arquivos foram atualizados

Isso garante consistência do delay da GOTV entre todos os configs do servidor.

---

## 18. Nomes de Times e Persistência por SteamID


---

## Observações Finais

- O plugin usa a classe `partial` do C#, dividido em múltiplos arquivos `.cs` para organização
- Todos os timers (`Timer?`) são cancelados corretamente nos eventos de `ResetMatchState()`, `ResetMapStates()` e `OnMapShutdown` para evitar execuções pendentes após troca de mapa
- O plugin é robusto a quedas de servidor graças ao Crash Recovery e ao `active_match.json` persistido
- Nomes de arquivo e pastas são sanitizados com `SanitizeFileName()` para remover caracteres inválidos
- O webhook Discord falha silenciosamente (apenas loga o erro) para não impactar o fluxo da partida
