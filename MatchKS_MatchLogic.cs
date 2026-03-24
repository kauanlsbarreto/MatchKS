using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Memory;

namespace MatchKS;

public partial class MatchKS
{


    
    [GameEventHandler]
    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        return HookResult.Continue;
    }
    public HookResult OnPlayerTeamChange(EventPlayerTeam @event, GameEventInfo info)
    {
        var newTeam = (CsTeam)@event.Team;
        var oldTeam = (CsTeam)@event.Oldteam;

        if (@event.Userid == null || !@event.Userid.IsValid || @event.Userid.IsBot) return HookResult.Continue;
        if (_activeMatch == null) return HookResult.Continue;

        var player = @event.Userid;

        if (oldTeam == CsTeam.Terrorist)
            _activeMatch.Team1.Players.Remove(player.SteamID.ToString());
        else if (oldTeam == CsTeam.CounterTerrorist)
            _activeMatch.Team2.Players.Remove(player.SteamID.ToString());

        if (newTeam == CsTeam.Terrorist)
            _activeMatch.Team1.Players[player.SteamID.ToString()] = player.PlayerName;
        else if (newTeam == CsTeam.CounterTerrorist)
            _activeMatch.Team2.Players[player.SteamID.ToString()] = player.PlayerName;

        return HookResult.Continue;    
    }
    private void StartMatch()
    {
        if (_isMatchLive) return;
        _isMatchLive = true;

        SyncLiveCfgFromPluginConfig();

        Server.ExecuteCommand("mp_warmup_end");
        Server.ExecuteCommand("exec MatchKS/live.cfg");

        var ffValue = _pluginConfig.FogoAmigo ? 1 : 0;
        var otValue = (_activeMatch?.EnableOvertime ?? _pluginConfig.EnableOvertime) ? 1 : 0;
        Server.ExecuteCommand($"mp_friendlyfire {ffValue}");
        Server.ExecuteCommand($"mp_overtime_enable {otValue}");
        Server.ExecuteCommand($"mp_overtime_startmoney {_pluginConfig.OvertimeStartMoney}");

        Server.PrintToChatAll($"{ChatPrefix} A partida está {ChatColors.Green}AO VIVO{ChatColors.Default}!");
        AddTimer(1.0f, () => Server.ExecuteCommand("mp_restartgame 1"));
    }

    private void HandleKnifeRoundEnd(CsTeam winner)
    {
        _isKnifeRoundActive = false;
        _knifeRoundWinnerTeam = winner;
        AddTimer(0.2f, StartSidePickPhase);
    }

    private void StartKnifeRound()
    {
        _isKnifeRoundActive = true;

        Server.ExecuteCommand("mp_warmup_end");
        Server.ExecuteCommand("exec MatchKS/knife.cfg");
        Server.ExecuteCommand("mp_startmoney 0; mp_maxmoney 0");
        Server.ExecuteCommand("mp_t_default_primary \"\"; mp_t_default_secondary \"\"");
        Server.ExecuteCommand("mp_ct_default_primary \"\"; mp_ct_default_secondary \"\"");
        Server.PrintToChatAll($"{ChatPrefix} A partida começará com um round de faca!");
        Server.PrintToChatAll($"{ChatPrefix} O time vencedor escolherá o lado.");

        AddTimer(1.0f, () => Server.ExecuteCommand("mp_restartgame 1"));
    }

    private void InitiateMatchProcess()
    {
        if (_isMatchLive) return;

        if (_isKnifeRoundEnabledForCurrentMap)
        {
            StartKnifeRound();
        }
        else
        {
            Server.PrintToChatAll($"{ChatPrefix} Round de faca desabilitado. A partida começará diretamente.");
            StartMatch(); 
        }
    }
    private void StartSidePickPhase()
    {
        _isSidePickPhase = true;
        _isSideSwapPending = false;
        _sidePickCountdown = 60;
        Server.ExecuteCommand("mp_pause_match");

        var winnerTeamName = GetTeamName((byte)_knifeRoundWinnerTeam!);

        Server.PrintToChatAll($"{ChatPrefix} O time {winnerTeamName} venceu o round de faca!");
        Server.PrintToChatAll($"{ChatPrefix} Digite {ChatColors.Lime}.stay{ChatColors.Default} ou {ChatColors.Lime}.switch{ChatColors.Default} (ou .ficar/.trocar).");

        _sidePickDisplayTimer?.Kill();
        _sidePickDisplayTimer = AddTimer(1.0f, UpdateSidePickCountdownHud, TimerFlags.REPEAT);

        _sidePickTimer = AddTimer(60.0f, ForceSidePickAndGoLive);
    }

    private void UpdateSidePickCountdownHud()
    {
        if (!_isSidePickPhase)
        {
            _sidePickDisplayTimer?.Kill();
            return;
        }

        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
        {
            p.PrintToCenterHtml(
                $"<font color='orange'>ESCOLHA DE LADO</font><br><font color='white'>.stay ou .switch</font><br><font size='6' color='lightgreen'>{_sidePickCountdown}s</font>"
            );
        }

        if (_sidePickCountdown > 0)
        {
            _sidePickCountdown--;
        }
    }


    private void ForceSidePickAndGoLive()
    {
        if (!_isSidePickPhase) return;

        Server.PrintToChatAll($"{ChatPrefix} O tempo para escolher o lado acabou!");
        
        HandleSidePickDecision(swapSides: false);
    }

    public void HandleSidePickDecision(bool swapSides)
    {
        _sidePickTimer?.Kill();
        _sidePickDisplayTimer?.Kill();
        _isSidePickPhase = false;

        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
        {
            p.PrintToCenterHtml(" ");
        }

        if (_activeMatch == null || _knifeRoundWinnerTeam == null) return;

        var choiceText = swapSides ? "trocar de lado" : "manter o lado inicial";
        Server.PrintToChatAll($"{ChatPrefix} O time vencedor decidiu {ChatColors.Lime}{choiceText}{ChatColors.Default}!");

        if (swapSides)
        {
            ApplyTrackedSideSwap();
            _isSideSwapPending = false;
            Server.ExecuteCommand("mp_swapteams");
            Server.PrintToChatAll($"{ChatPrefix} Os times trocaram de lado!");
        }

        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Gold}{_activeMatch.Team1.Name}{ChatColors.Default} começará como {ChatColors.Gold}TR{ChatColors.Default}.");
        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.LightBlue}{_activeMatch.Team2.Name}{ChatColors.Default} começará como {ChatColors.LightBlue}CT{ChatColors.Default}.");

        AddTimer(1.5f, () => {
            Server.ExecuteCommand("mp_unpause_match");
            StartMatch();
        });
    }

    [GameEventHandler]
    public HookResult OnTeamSwap(EventCsWinPanelRound @event, GameEventInfo info)
    {
        if (_activeMatch == null || !_isSideSwapPending)
            return HookResult.Continue;

        ApplyTrackedSideSwap();
        _isSideSwapPending = false;
        return HookResult.Continue;
    }

    private void ApplyTrackedSideSwap()
    {
        if (_activeMatch == null) return;

        // Não troca Team1/Team2, apenas ajusta estados de pausa se necessário
        if (_pausingTeam == CsTeam.Terrorist) _pausingTeam = CsTeam.CounterTerrorist;
        else if (_pausingTeam == CsTeam.CounterTerrorist) _pausingTeam = CsTeam.Terrorist;

        if (_pausingTeamScheduled == CsTeam.Terrorist) _pausingTeamScheduled = CsTeam.CounterTerrorist;
        else if (_pausingTeamScheduled == CsTeam.CounterTerrorist) _pausingTeamScheduled = CsTeam.Terrorist;

        if (_techPauseTeam == CsTeam.Terrorist) _techPauseTeam = CsTeam.CounterTerrorist;
        else if (_techPauseTeam == CsTeam.CounterTerrorist) _techPauseTeam = CsTeam.Terrorist;

        if (_techPauseScheduledTeam == CsTeam.Terrorist) _techPauseScheduledTeam = CsTeam.CounterTerrorist;
        else if (_techPauseScheduledTeam == CsTeam.CounterTerrorist) _techPauseScheduledTeam = CsTeam.Terrorist;
    }

    private void SwapTeams(MatchTeam team, CsTeam side)
    {
        foreach (var playerSteamId in team.Players.Keys)
        {
            var player = Utilities.GetPlayerFromSteamId(ulong.Parse(playerSteamId));
            if (player != null && player.IsValid)
            {
                player.SwitchTeam(side);
            }
        }
    }

    private string GetTeamName(byte teamNum)
    {
        if (teamNum == (byte)CsTeam.Terrorist) return _activeMatch?.Team1.Name ?? "Terroristas";
        if (teamNum == (byte)CsTeam.CounterTerrorist) return _activeMatch?.Team2.Name ?? "Contra-Terroristas";
        return "Desconhecido";
    }

    private void AnnounceReadyStatus()
    {
        if (_isMatchLive) { _readyStatusTimer?.Kill(); return; }

        int terroristsCount = Utilities.GetPlayers().Count(p => p.TeamNum == (byte)CsTeam.Terrorist && p.IsValid && !p.IsBot);
        int ctsCount = Utilities.GetPlayers().Count(p => p.TeamNum == (byte)CsTeam.CounterTerrorist && p.IsValid && !p.IsBot);

        var waitingFor = new List<string>();
        if (!_isTeamTReady) { string teamName = _activeMatch?.Team1.Name ?? "TR"; if (terroristsCount < 5) waitingFor.Add($"{teamName} ({terroristsCount}/5)"); else waitingFor.Add($"{teamName} (!ready)"); }
        if (!_isTeamCTReady) { string teamName = _activeMatch?.Team2.Name ?? "CT"; if (ctsCount < 5) waitingFor.Add($"{teamName} ({ctsCount}/5)"); else waitingFor.Add($"{teamName} (!ready)"); }
        if (waitingFor.Any()) { Server.PrintToChatAll($"{MatchKS.ChatPrefix} Aguardando: {ChatColors.Gold}{string.Join(", ", waitingFor)}"); }
    }

    private void AnnounceMatchCommands()
    {
        var c = ChatColors.Default;
        var p = MatchKS.ChatPrefixColor;

        var knifeStatus = _isKnifeRoundEnabledForCurrentMap
            ? $"{ChatColors.Green}SIM"
            : $"{ChatColors.Red}NAO";
        var ffStatus = _pluginConfig.FogoAmigo
            ? $"{ChatColors.Red}SIM"
            : $"{ChatColors.Green}NAO";
        var otStatus = (_activeMatch?.EnableOvertime ?? _pluginConfig.EnableOvertime)
            ? $"{ChatColors.Green}SIM"
            : $"{ChatColors.Red}NAO";

        Server.PrintToChatAll($"{MatchKS.ChatPrefix} {p}━━━━━━━━━━━━━━━━━━━━━━━━━━━{c}");
        Server.PrintToChatAll($"{MatchKS.ChatPrefix} {p}COMANDOS DA PARTIDA{c}");
        Server.PrintToChatAll($"{MatchKS.ChatPrefix} {p}.ready{c} ou {p}.r{c} — marcar pronto");
        Server.PrintToChatAll($"{MatchKS.ChatPrefix} {p}.nometime <nome>{c} — definir nome do time");
        Server.PrintToChatAll($"{MatchKS.ChatPrefix} {p}.tac{c} — pausa tatica ({p}{_pluginConfig.PausesTaticoPorEquipe}{c} por time, {p}{_pluginConfig.DuracaoPauseTatico}s{c} cada)");
        Server.PrintToChatAll($"{MatchKS.ChatPrefix} Round de faca: {knifeStatus}{c} | Fogo amigo: {ffStatus}{c} | Overtime: {otStatus}{c}");
        Server.PrintToChatAll($"{MatchKS.ChatPrefix} {p}━━━━━━━━━━━━━━━━━━━━━━━━━━━{c}");
    }

    private void CheckIfMatchCanStart()
    {
        if (_activeMatch == null) return; 

        bool areTeamsNamed = !string.IsNullOrEmpty(_activeMatch.Team1.Name) && 
                             !string.IsNullOrEmpty(_activeMatch.Team2.Name);

        if (areTeamsNamed && _isTeamTReady && _isTeamCTReady)
        {
            InitiateMatchProcess(); 
        }
        else
        {
            var waitingFor = new List<string>();
            if (!areTeamsNamed) waitingFor.Add("Nomes dos Times (!time1 <nome_ct> e !time2 <nome_tr>)");
            if (!_isTeamTReady) waitingFor.Add("Time TR (!ready)");
            if (!_isTeamCTReady) waitingFor.Add("Time CT (!ready)");

            if (waitingFor.Count > 0)
            {
                Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Red}Partida em espera.{ChatColors.Default} Aguardando: {ChatColors.Gold}{string.Join(", ", waitingFor)}");
            }
        }
    }

    private void ResetMapStates()
    {
        _isTeamTReady = false; _isTeamCTReady = false; _isMatchLive = false; _isKnifeRoundActive = false; _isPauseActive = false;
        _knifeRoundWinnerTeam = null;
        _team1TacPausesUsed = 0; // Reset only on new map
        _team2TacPausesUsed = 0; // Reset only on new map
        // removed custom team name owner logic
        _isSidePickPhase = false;
        _isSideSwapPending = false;
        _sidePickCountdown = 0;
        _sidePickTimer?.Kill();
        _sidePickDisplayTimer?.Kill();
        _isPauseScheduled = false;
        _isTechPauseScheduled = false;
        _isTechPauseActive = false;
        _pausingTeam = null;
        _pausingTeamScheduled = CsTeam.None;
        _techPauseTeam = CsTeam.None;
        _techPauseScheduledTeam = CsTeam.None;
        _pauseCountdown = 0;

        _autoSideSwapWindowTimer?.Kill();
        _tacTimer?.Kill();
        _pauseDisplayTimer?.Kill();
        _isWaitingForRestoreReady = false;
        _isTeamTRestoreReady = false;
        _isTeamCTRestoreReady = false;
        _restoreReadyDisplayTimer?.Kill();
    }


    private void CheckCompetitiveMode()
    {
        var gameType = ConVar.Find("game_type"); var gameMode = ConVar.Find("game_mode");
        if (gameType == null || gameMode == null || gameType.GetPrimitiveValue<int>() != 0 || gameMode.GetPrimitiveValue<int>() != 1) { Server.PrintToChatAll($"{MatchKS.ChatPrefix} O jogo não está no modo competitivo!"); }
    }
}
