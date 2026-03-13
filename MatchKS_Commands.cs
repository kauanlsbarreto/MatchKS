using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Cvars;
using System.Linq;
using System;
using System.IO;

namespace MatchKS;

public partial class MatchKS
{

    private bool IsInFreezetime()
    {
        var gameRules = GetGameRules();
        return gameRules is { FreezePeriod: true };
    }
    private CCSGameRules? GetGameRules()
    {
        var gameRulesEntities = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules");
        return gameRulesEntities.FirstOrDefault()?.GameRules;
    }

    [ConsoleCommand("css_map"), RequiresPermissions("@css/kick")]
    public void OnMapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            player?.PrintToChat("Uso: .map <nome_do_mapa>");
            return;
        }

        var mapName = command.GetArg(1);
        
        AddTimer(0.5f, () =>
        {
            Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} está trocando o mapa para {ChatColors.Green}{mapName}{ChatColors.Default}.");
        });

        Server.ExecuteCommand($"changelevel \"{mapName}\"");
    }

    [ConsoleCommand("css_kill"), RequiresPermissions("@css/kick")]
    public void OnKillCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        var adminTeam = player.TeamNum;
        if (adminTeam != (byte)CsTeam.Terrorist && adminTeam != (byte)CsTeam.CounterTerrorist)
        {
            player.PrintToChat($"{ChatPrefix} Você precisa estar em um time (CT ou TR) para usar este comando.");
            return;
        }

        var enemyTeam = adminTeam == (byte)CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        string enemyTeamName = enemyTeam == CsTeam.CounterTerrorist ? "Contra-Terroristas" : "Terroristas";

        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} eliminou o time {enemyTeamName}!");

        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && p.TeamNum == (byte)enemyTeam))
        {
            p.PlayerPawn.Value?.CommitSuicide(false, true);
        }
    }

    [ConsoleCommand("css_slap"), RequiresPermissions("@css/kick")]
    public void OnSlapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;

        var players = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot).ToList();

        if (command.ArgCount < 2)
        {
            player.PrintToChat($"{ChatPrefix} Jogadores disponíveis para slap:");
            for (int i = 0; i < players.Count; i++)
            {
                player.PrintToChat($" {ChatColors.Green}{i + 1}{ChatColors.Default}: {players[i].PlayerName}");
            }
            player.PrintToChat($"{ChatPrefix} Uso: {ChatColors.Green}.slap <número> [tapas]{ChatColors.Default}");
            return;
        }

        if (!int.TryParse(command.GetArg(1), out int playerNumber) || playerNumber < 1 || playerNumber > players.Count)
        {
            player.PrintToChat($"{ChatPrefix} Número de jogador inválido. Use {ChatColors.Green}.slap{ChatColors.Default} para ver a lista.");
            return;
        }

        int slapCount = command.ArgCount > 2 && int.TryParse(command.GetArg(2), out int parsedSlaps) ? parsedSlaps : 1;
        if (slapCount < 1) slapCount = 1;

        var targetPlayer = players[playerNumber - 1];

        if (targetPlayer.PawnIsAlive)
        {
            Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} deu {slapCount} tapa(s) em {ChatColors.Lime}{targetPlayer.PlayerName}{ChatColors.Default}!");

            for (int i = 0; i < slapCount; i++)
            {
                AddTimer(i * 0.2f, () => {
                    if (targetPlayer.PawnIsAlive && targetPlayer.PlayerPawn.Value != null)
                        targetPlayer.PlayerPawn.Value.AbsVelocity.Z += 350.0f;
                });
            }
        }
    }


    [ConsoleCommand("css_time1"), CommandHelper(minArgs: 1, usage: "<nome>")]
    [RequiresPermissions("@css/kick")]
    public void OnSetTeam1NameCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_activeMatch == null)
        {
            player?.PrintToChat($"{MatchKS.ChatPrefix} Nenhuma partida está ativa/configurada.");
            return;
        }

        var teamName = command.ArgString.Split(' ', 2).Length > 1 ? command.ArgString.Split(' ', 2)[1].Trim() : command.GetArg(1);

        if (string.IsNullOrEmpty(teamName))
        {
            player?.PrintToChat($"{MatchKS.ChatPrefix} Uso: !time1 <nome>");
            return;
        }

        _activeMatch.Team2.Name = teamName; 
        Server.ExecuteCommand($"mp_teamname_1 \"{teamName}\""); 


        Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} definiu o time {ChatColors.LightBlue}CT (time1){ChatColors.Default} para: {ChatColors.LightBlue}{teamName}");
        CheckIfMatchCanStart();
    }

    [ConsoleCommand("css_time2"), CommandHelper(minArgs: 1, usage: "<nome>")] 
    [RequiresPermissions("@css/kick")]
    public void OnSetTeam2NameCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_activeMatch == null)
        {
            player?.PrintToChat($"{MatchKS.ChatPrefix} Nenhuma partida está ativa/configurada.");
            return;
        }

        var teamName = command.ArgString.Split(' ', 2).Length > 1 ? command.ArgString.Split(' ', 2)[1].Trim() : command.GetArg(1);

        if (string.IsNullOrEmpty(teamName))
        {
            player?.PrintToChat($"{MatchKS.ChatPrefix} Uso: !time2 <nome>");
            return;
        }

        _activeMatch.Team1.Name = teamName;
        Server.ExecuteCommand($"mp_teamname_2 \"{teamName}\"");

        Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} definiu o time {ChatColors.Gold}TR (time2){ChatColors.Default} para: {ChatColors.Gold}{teamName}");
        CheckIfMatchCanStart();
    }

    [ConsoleCommand("css_config")]
    public void OnMatchConfigCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_activeMatch == null)
        {
            player?.PrintToChat($"{MatchKS.ChatPrefix} Nenhuma partida está ativa/configurada.");
            return;
        }
        
        string roundsParaGanhar = "Não Definido";
        int boX = _activeMatch.MapList.Count; 
        if (boX > 0)
        {
            int mapsToWin = (boX / 2) + 1;
            roundsParaGanhar = $"{mapsToWin} mapa(s) (Série Bo{boX})";
        }

        string overtimeStatus = _activeMatch.EnableOvertime ? $"{ChatColors.Green}Liberado" : $"{ChatColors.Red}Bloqueado";

        var tvAutoRecordCvar = ConVar.Find("tv_autorecord");
        string demoStatus = tvAutoRecordCvar != null && tvAutoRecordCvar.GetPrimitiveValue<bool>()
                            ? $"{ChatColors.Green}SIM (tv_autorecord 1)"
                            : $"{ChatColors.Red}NÃO (tv_autorecord 0)";

        string configDisplay = $"{ChatColors.Default}================== {ChatColors.Yellow}CONFIGURAÇÃO DO MATCH{ChatColors.Default} ==================\r\n" +
                               $"{ChatColors.Default}  • Modo de Jogo: {ChatColors.Yellow}{_activeMatch.GameMode}{ChatColors.Default}\r\n" +
                               $"{ChatColors.Default}  • Rounds para Vencer a Série: {ChatColors.Yellow}{roundsParaGanhar}{ChatColors.Default}\r\n" +
                               $"{ChatColors.Default}  • Overtime: {overtimeStatus}{ChatColors.Default}\r\n" +
                               $"{ChatColors.Default}  • Gravação de DEMO: {demoStatus}{ChatColors.Default}\r\n" +
                               $"{ChatColors.Default}  • {ChatColors.Gold}Time 1 ({_activeMatch.Team1.Name}){ChatColors.Default}: {ChatColors.Gold}{_activeMatch.Team1.MapsWon}{ChatColors.Default} mapa(s) vencido(s)\r\n" +
                               $"{ChatColors.Default}  • {ChatColors.LightBlue}Time 2 ({_activeMatch.Team2.Name}){ChatColors.Default}: {ChatColors.LightBlue}{_activeMatch.Team2.MapsWon}{ChatColors.Default} mapa(s) vencido(s)\r\n" +
                               $"{ChatColors.Default}=======================================================";

        player?.PrintToChat(configDisplay);
    }

    [ConsoleCommand("css_start"), ConsoleCommand("css_comecar"), RequiresPermissions("@css/kick")]
    public void OnForceStartCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_activeMatch == null || _isMatchLive)
        {
            player?.PrintToChat($"{MatchKS.ChatPrefix} A partida não pode ser forçada agora.");
            return;
        }

        Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} forçou o início da partida!");
        _isTeamTReady = true;
        _isTeamCTReady = true;
        CheckIfMatchCanStart();
    }

    private void HandleSidePickCommand(CCSPlayerController? player, bool swapSides)
    {
        if (player == null || !player.IsValid) return;

        if (!_isSidePickPhase)
        {
            player.PrintToChat($"{ChatPrefix} Não é o momento de escolher os lados.");
            return;
        }

        if (_knifeRoundWinnerTeam == null || player.TeamNum != (byte)_knifeRoundWinnerTeam)
        {
            player.PrintToChat($"{ChatPrefix} Apenas o time que venceu o round de faca pode escolher o lado.");
            return;
        }
        
        HandleSidePickDecision(swapSides);
    }

    [ConsoleCommand("css_restart"), ConsoleCommand("css_recomecar"), RequiresPermissions("@css/kick")]
    public void OnRestartMatchCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_activeMatch == null)
        {
            player?.PrintToChat($"{MatchKS.ChatPrefix} Nenhuma partida ativa para reiniciar.");
            return;
        }

        Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} reiniciou a partida de volta para o aquecimento.");

        Server.ExecuteCommand("tv_stoprecord");

        ResetMapStates();

        Server.ExecuteCommand("exec MatchKS/warmup.cfg");

        AddTimer(1.0f, () =>
        {
            Server.ExecuteCommand("mp_restartgame 1");
            Server.PrintToChatAll($"{MatchKS.ChatPrefix} Partida reiniciada. Aguardando prontidão dos times.");
        });

        _readyStatusTimer?.Kill();
        _readyStatusTimer = AddTimer(15.0f, AnnounceReadyStatus, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    [ConsoleCommand("css_adm"), RequiresPermissions("@css/kick")]
    public void OnAdminChatCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2) { player?.PrintToChat("Uso: .adm <mensagem>"); return; }
        var message = command.ArgString.Substring(command.GetArg(0).Length).Trim();
        Server.PrintToChatAll($" {ChatColors.Red}[ADM - MatchKS]{ChatColors.Default} {message}");
    }

    [ConsoleCommand("css_nometime")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnNoTimeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || _isMatchLive) return;

        if (command.ArgCount < 2)
        {
            player.PrintToChat($"{ChatPrefix} Uso: .nometime <nome_do_time>");
            return;
        }
        
        var newName = command.ArgString.Split(' ', 2).Length > 1 ? command.ArgString.Split(' ', 2)[1].Trim() : command.GetArg(1);
        var teamNum = player.TeamNum;

        if (teamNum == (byte)CsTeam.CounterTerrorist)
        {
            _activeMatch!.Team2.Name = newName;
            _isCtNameCustom = true;
            _ctNamerSteamId = player.SteamID;
            SetTeamNameOwner(player.SteamID, newName);
            Server.ExecuteCommand($"mp_teamname_1 \"{newName}\"");
            Server.PrintToChatAll($"{ChatPrefix} O nome do time Contra-Terrorista foi definido para: {ChatColors.Green}{newName}");
        }
        else if (teamNum == (byte)CsTeam.Terrorist)
        {
            _activeMatch!.Team1.Name = newName;
            _isTrNameCustom = true;
            _trNamerSteamId = player.SteamID;
            SetTeamNameOwner(player.SteamID, newName);
            Server.ExecuteCommand($"mp_teamname_2 \"{newName}\"");
            Server.PrintToChatAll($"{ChatPrefix} O nome do time Terrorista foi definido para: {ChatColors.Green}{newName}");
        }
    }

    [ConsoleCommand("css_rk"), RequiresPermissions("@css/root")]
    public void OnKnifeRoundToggle(CCSPlayerController? player, CommandInfo command)
    {
        _isKnifeRoundEnabledForCurrentMap = !_isKnifeRoundEnabledForCurrentMap;
        Server.PrintToChatAll($"{MatchKS.ChatPrefix} Admin Override: Round de Faca para este mapa está agora {(_isKnifeRoundEnabledForCurrentMap ? "HABILITADO" : "DESABILITADO")}.");
    }
    
    [ConsoleCommand("css_skinsperso"), RequiresPermissions("@css/kick")]
    public void OnToggleSkinCheckerCommand(CCSPlayerController? player, CommandInfo command)
    {
        _isSkinCheckerEnabled = !_isSkinCheckerEnabled;
        string status = _isSkinCheckerEnabled ? "ATIVADA" : "DESATIVADA";
        string statusColor = _isSkinCheckerEnabled ? ChatColors.Green.ToString() : ChatColors.Red.ToString();

        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} alterou a verificação de skins. Status: {statusColor}{status}");
    }

    [ConsoleCommand("css_nobots"), RequiresPermissions("@css/kick")]
    public void OnNoBotsCommand(CCSPlayerController? player, CommandInfo command)
    {
        Server.ExecuteCommand("bot_join_after_player 0");
        Server.ExecuteCommand("bot_kick");
        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} removeu os bots. Bots não entrarão automaticamente.");
    }

    [ConsoleCommand("css_bots"), RequiresPermissions("@css/kick")]
    public void OnBotsCommand(CCSPlayerController? player, CommandInfo command)
    {
        Server.ExecuteCommand("bot_quota_mode normal");
        Server.ExecuteCommand("bot_join_after_player 1");

        int humanPlayers = Utilities.GetPlayers().Count(p => p.IsValid && !p.IsBot && p.Connected == PlayerConnectedState.PlayerConnected);
        int botsNeeded = 10 - humanPlayers;
        if (botsNeeded < 0) botsNeeded = 0;

        Server.ExecuteCommand($"bot_quota {botsNeeded}");
        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} adicionou bots para completar os times.");
    }

    [ConsoleCommand("css_stay"), ConsoleCommand("css_ficar")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnStayCommand(CCSPlayerController? player, CommandInfo command)
    {
        HandleSidePickCommand(player, swapSides: false);
    }

    [ConsoleCommand("css_switch"), ConsoleCommand("css_trocar")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSwitchCommand(CCSPlayerController? player, CommandInfo command)
    {
        HandleSidePickCommand(player, swapSides: true);
    }

    
    [ConsoleCommand("css_tec"), ConsoleCommand("css_tech"), RequiresPermissions("@css/kick")]
    public void OnTechPauseCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_isMatchLive) return;
        
        if (_isTechPauseActive)
        {
            UnpauseMatch(isTechPause: true);
        }
        else 
        {
            if (_isPauseActive)
            {
                player?.PrintToChat($"{ChatPrefix} Uma pausa tática já está em andamento.");
                return;
            }

            if (IsInFreezetime())
            {
                StartTechPause();
            }
            else
            {
                _isTechPauseScheduled = true;
                Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} agendou uma pausa técnica para o próximo round.");
            }
        }
    }
    
    [ConsoleCommand("css_tac"), ConsoleCommand("css_timeout")]
    public void OnTacCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !_isMatchLive || _isPauseActive || _isPauseScheduled || _isTechPauseActive || _isTechPauseScheduled)
        {
            player?.PrintToChat($"{ChatPrefix} Não é possível pedir pausa agora.");
            return;
        }

        int pausesUsed = player.TeamNum == (byte)CsTeam.Terrorist ? _team1TacPausesUsed : _team2TacPausesUsed;
        if (pausesUsed >= _pluginConfig.PausesTaticoPorEquipe)
        {
            player.PrintToChat($"{ChatPrefix} Seu time não tem mais pausas táticas.");
            return;
        }

        _pausingTeamScheduled = (CsTeam)player.TeamNum;

        if (IsInFreezetime())
        {
            StartTacticalPause(); 
        }
        else
        {
            _isPauseScheduled = true;
            var teamName = GetTeamName(player.TeamNum);
            Server.PrintToChatAll($"{ChatPrefix} O time {teamName} agendou uma pausa tática para o próximo round.");
        }
    }
    
    private void StartTechPause()
    {
        _isTechPauseActive = true;
        Server.ExecuteCommand("mp_pause_match");
        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} pausou a partida (pausa técnica). Use {ChatColors.Green}.tec{ChatColors.Default} para despausar.");
        
        _pauseDisplayTimer?.Kill();
        _pauseDisplayTimer = AddTimer(1.0f, () =>
        {
            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
            {
                p.PrintToCenterHtml("<font color='orange'>PARTIDA EM PAUSA TÉCNICA</font><br><font color='white'>Admin, digite .tec para continuar</font>");
            }
        }, TimerFlags.REPEAT);
    }

    private void StartTacticalPause()
    {
        _isPauseActive = true;
        if (_pausingTeamScheduled == CsTeam.Terrorist) _team1TacPausesUsed++; else _team2TacPausesUsed++;
        _pausingTeam = _pausingTeamScheduled;
        
        Server.ExecuteCommand("mp_pause_match");
        
        var teamName = GetTeamName((byte)_pausingTeam);
        int pausesUsed = _pausingTeam == CsTeam.Terrorist ? _team1TacPausesUsed : _team2TacPausesUsed;

        Server.PrintToChatAll($"{ChatPrefix} O time {teamName} iniciou uma pausa tática. ({pausesUsed}/{_pluginConfig.PausesTaticoPorEquipe})");
        
        _pauseCountdown = _pluginConfig.DuracaoPauseTatico;
        
        _pauseDisplayTimer?.Kill();
        _pauseDisplayTimer = AddTimer(1.0f, OnPauseTimerTick, TimerFlags.REPEAT);

        _tacTimer?.Kill();
        _tacTimer = AddTimer((float)_pluginConfig.DuracaoPauseTatico, () => UnpauseMatch(isTechPause: false));
    }

    private void OnPauseTimerTick()
    {
        if (_pauseCountdown > 0)
        {
            var teamName = GetTeamName((byte)_pausingTeam!);
            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
            {
                 p.PrintToCenterHtml($"<font color='lightblue'>PAUSA TÁTICA | {teamName}</font><br><font size='6' color='white'>{_pauseCountdown}s</font>");
            }
            _pauseCountdown--;
        }
    }
    
    public void UnpauseMatch(bool isTechPause)
    {
        _pauseDisplayTimer?.Kill();
        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
        {
            p.PrintToCenterHtml(" ");
        }

        if (isTechPause)
        {
            _isTechPauseActive = false;
            Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} despausou a partida!");
        }
        else
        {
            _isPauseActive = false;
            _tacTimer?.Kill();
            Server.PrintToChatAll($"{ChatPrefix} A pausa tática terminou. A partida foi despausada!");
        }
        
        Server.ExecuteCommand("mp_unpause_match");
    }

    private bool IsReadyAdminOverride(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid)
        {
            return true;
        }

        return AdminManager.PlayerHasPermissions(player, "@css/kick");
    }

    private void StartRestorePauseFlow(int targetRound)
    {
        _isPauseScheduled = false;
        _isTechPauseScheduled = false;
        _isPauseActive = false;
        _isTechPauseActive = false;
        _tacTimer?.Kill();
        _pauseDisplayTimer?.Kill();

        _isRestorePauseActive = true;
        _isRestoreReadyT = false;
        _isRestoreReadyCT = false;

        AddTimer(0.6f, () =>
        {
            Server.ExecuteCommand("mp_pause_match");
            Server.PrintToChatAll($"{ChatPrefix} Restore do round {ChatColors.Green}{targetRound}{ChatColors.Default} carregado e partida pausada.");
            Server.PrintToChatAll($"{ChatPrefix} Para continuar: um jogador de cada time deve usar {ChatColors.Lime}.rrlive{ChatColors.Default}.");
            Server.PrintToChatAll($"{ChatPrefix} Admin pode liberar direto com {ChatColors.Lime}.ready{ChatColors.Default}.");
        });
    }

    private void ReleaseRestorePause(string actor)
    {
        _isRestorePauseActive = false;
        _isRestoreReadyT = false;
        _isRestoreReadyCT = false;
        Server.ExecuteCommand("mp_unpause_match");
        Server.PrintToChatAll($"{ChatPrefix} Restore confirmado por {ChatColors.Green}{actor}{ChatColors.Default}. Partida retomada!");
    }

    private void RegisterRestoreReady(CCSPlayerController player)
    {
        if (!_isRestorePauseActive)
        {
            player.PrintToChat($"{ChatPrefix} Não existe restore pendente para liberar.");
            return;
        }

        if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist)
        {
            player.PrintToChat($"{ChatPrefix} Entre em TR ou CT para confirmar o restore.");
            return;
        }

        if (player.TeamNum == (byte)CsTeam.Terrorist)
        {
            if (_isRestoreReadyT)
            {
                player.PrintToChat($"{ChatPrefix} Seu time ja confirmou com .rrlive.");
                return;
            }

            _isRestoreReadyT = true;
            Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Gold}{GetTeamName((byte)CsTeam.Terrorist)}{ChatColors.Default} confirmou com .rrlive.");
        }
        else
        {
            if (_isRestoreReadyCT)
            {
                player.PrintToChat($"{ChatPrefix} Seu time ja confirmou com .rrlive.");
                return;
            }

            _isRestoreReadyCT = true;
            Server.PrintToChatAll($"{ChatPrefix} {ChatColors.LightBlue}{GetTeamName((byte)CsTeam.CounterTerrorist)}{ChatColors.Default} confirmou com .rrlive.");
        }

        if (_isRestoreReadyT && _isRestoreReadyCT)
        {
            ReleaseRestorePause("ambos os times");
            return;
        }

        var waitingFor = new System.Collections.Generic.List<string>();
        if (!_isRestoreReadyT) waitingFor.Add(GetTeamName((byte)CsTeam.Terrorist));
        if (!_isRestoreReadyCT) waitingFor.Add(GetTeamName((byte)CsTeam.CounterTerrorist));
        Server.PrintToChatAll($"{ChatPrefix} Aguardando .rrlive de: {ChatColors.Green}{string.Join(", ", waitingFor)}");
    }

    [ConsoleCommand("css_r"), ConsoleCommand("css_ready"), ConsoleCommand("css_pronto")]
    public void OnUnifiedReadyCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_isRestorePauseActive)
        {
            if (IsReadyAdminOverride(player))
            {
                var actor = player?.IsValid == true ? player.PlayerName : "CONSOLE";
                ReleaseRestorePause(actor);
                return;
            }

            player?.PrintToChat($"{ChatPrefix} Apenas admin pode usar .ready para liberar restore. Use .rrlive pelo seu time.");
            return;
        }

        if (player == null || !player.IsValid || _isMatchLive) return;

        int teamPlayerCount = Utilities.GetPlayers().Count(p => p.TeamNum == player.TeamNum && p.IsValid && !p.IsBot && p.Connected == PlayerConnectedState.PlayerConnected);

        if (teamPlayerCount < 5)
        {
            player.PrintToChat($"{MatchKS.ChatPrefix} Seu time precisa ter pelo menos 5 jogadores para poder dar pronto.");
            return;
        }

        if (player.TeamNum == (byte)CsTeam.Terrorist && !_isTeamTReady)
        {
            _isTeamTReady = true;
            Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Olive}{player.PlayerName}{ChatColors.Default} confirmou o pronto para o time {ChatColors.Gold}TERRORISTA!");
            CheckIfMatchCanStart();
        }
        else if (player.TeamNum == (byte)CsTeam.CounterTerrorist && !_isTeamCTReady)
        {
            _isTeamCTReady = true;
            Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Olive}{player.PlayerName}{ChatColors.Default} confirmou o pronto para o time {ChatColors.LightBlue}CONTRA-TERRORISTA!");
            CheckIfMatchCanStart();
        }
    }

    [ConsoleCommand("css_rrlive")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRestoreReadyCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;
        RegisterRestoreReady(player);
    }

    [ConsoleCommand("css_backups"), RequiresPermissions("@css/kick")]
    public void OnBackupsListCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || _activeMatch == null) return;
        
        var backupFiles = Directory.GetFiles(BackupFolderPath, "*.txt");
        if (backupFiles.Length == 0)
        {
            player.PrintToChat($"{ChatPrefix} Nenhum arquivo de backup encontrado.");
            return;
        }

        player.PrintToChat($"{ChatPrefix} Backups disponíveis para a partida atual:");

        var team1Name = SanitizeFileName(_activeMatch.Team1.Name);
        var team2Name = SanitizeFileName(_activeMatch.Team2.Name);
        var mapName = SanitizeFileName(Server.MapName);

        var matchIdentifier1 = $"_{team1Name}_{team2Name}_{mapName}_";
        var matchIdentifier2 = $"_{team2Name}_{team1Name}_{mapName}_";

        var matchFiles = backupFiles
            .Where(f => {
                var fn = Path.GetFileName(f);
                return fn.Contains(matchIdentifier1) || fn.Contains(matchIdentifier2);
            })
            .OrderBy(f => f)
            .ToList();

        if (matchFiles.Count == 0)
        {
            player.PrintToChat(" Nenhum backup encontrado para esta partida e mapa.");
            return;
        }

        foreach (var file in matchFiles)
        {
            var fn = Path.GetFileNameWithoutExtension(file);
            var identifier = fn.Contains(matchIdentifier1) ? matchIdentifier1 : matchIdentifier2;
            var partsAfterIdentifier = fn.Substring(fn.IndexOf(identifier) + identifier.Length).Split('_');

            if (partsAfterIdentifier.Length > 0 && int.TryParse(partsAfterIdentifier[0], out int roundInFile))
            {
                player.PrintToChat($" Round {ChatColors.Green}{roundInFile}{ChatColors.Default}: {Path.GetFileName(file)}");
            }
        }
        player.PrintToChat($"{ChatPrefix} Use {ChatColors.Green}.restore <round>{ChatColors.Default} para carregar um backup.");
    }

    
    [ConsoleCommand("css_restore"), RequiresPermissions("@css/kick")]
    public void OnRestoreCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_isMatchLive || _activeMatch == null) 
        { 
            player?.PrintToChat($"{MatchKS.ChatPrefix} A partida precisa estar ao vivo para restaurar um backup."); 
            return; 
        }
        if (command.ArgCount < 2) 
        { 
            player?.PrintToChat($"{MatchKS.ChatPrefix} Uso: .restore <round>"); 
            player?.PrintToChat($"{MatchKS.ChatPrefix} Use .backups para listar os rounds disponíveis."); 
            return; 
        }

        if (!int.TryParse(command.GetArg(1), out int targetRound) || targetRound < 0)
        {
            player?.PrintToChat($"{MatchKS.ChatPrefix} Número do round inválido.");
            return;
        }

        var backupFiles = Directory.GetFiles(BackupFolderPath, "*.txt");
        if (backupFiles.Length == 0)
        {
            player?.PrintToChat($"{ChatPrefix} Nenhum arquivo de backup foi encontrado no servidor.");
            return;
        }

        var team1Name = SanitizeFileName(_activeMatch.Team1.Name);
        var team2Name = SanitizeFileName(_activeMatch.Team2.Name);
        var mapName = SanitizeFileName(Server.MapName);

        var matchIdentifier1 = $"_{team1Name}_{team2Name}_{mapName}_";
        var matchIdentifier2 = $"_{team2Name}_{team1Name}_{mapName}_";

        var filesForRound = backupFiles.Where(file => {
            var fn = Path.GetFileNameWithoutExtension(file);
            var identifier = fn.Contains(matchIdentifier1) ? matchIdentifier1 : matchIdentifier2;

            if (string.IsNullOrEmpty(identifier) || !fn.Contains(identifier)) return false;

            var partsAfter = fn.Substring(fn.IndexOf(identifier) + identifier.Length).Split('_');
            return partsAfter.Length > 0 && int.TryParse(partsAfter[0], out int roundInFile) && roundInFile == targetRound;
        }).ToList();
        
        if (filesForRound.Count > 0)
        {
            var fileToRestore = filesForRound.OrderByDescending(f => f).First(); 
            var fileNameOnly = Path.GetFileName(fileToRestore); 
            var relativePath = Path.Join("BackupMatchKS", fileNameOnly).Replace('\\', '/');
            
            Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} está restaurando a partida para o backup do round {targetRound}.");
            Server.PrintToChatAll($"{MatchKS.ChatPrefix} Carregando arquivo: {relativePath}");
            _currentRoundBackupFile = string.Empty;
            Server.ExecuteCommand($"mp_backup_restore_load_file \"{relativePath}\"");
            StartRestorePauseFlow(targetRound);
        }
        else
        {
            player?.PrintToChat($"{MatchKS.ChatPrefix} Nenhum backup encontrado para o round {targetRound} da partida atual.");
        }
    }


    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (_isPauseScheduled)
        {
            _isPauseScheduled = false;
            StartTacticalPause();
        }
        else if (_isTechPauseScheduled)
        {
            _isTechPauseScheduled = false;
            StartTechPause();
        }

        return HookResult.Continue;
    }
}

