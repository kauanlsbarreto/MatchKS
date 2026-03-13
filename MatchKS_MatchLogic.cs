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
    private void SetTeamNameOwner(ulong steamId, string teamName)
    {
        var key = steamId.ToString();
        _teamNameOwnerConfig.SteamIdToTeamName[key] = teamName;
        SaveTeamNameOwnerConfig();
    }

    private bool TryGetOwnedTeamName(ulong steamId, out string teamName)
    {
        return _teamNameOwnerConfig.SteamIdToTeamName.TryGetValue(steamId.ToString(), out teamName!);
    }

    private void ApplySideName(CsTeam side, string name)
    {
        if (_activeMatch == null) return;

        if (side == CsTeam.Terrorist)
        {
            _activeMatch.Team1.Name = name;
            _isTrNameCustom = !string.Equals(name, GetDefaultTeamName(CsTeam.Terrorist), StringComparison.OrdinalIgnoreCase);
            Server.ExecuteCommand($"mp_teamname_2 \"{name}\"");
        }
        else if (side == CsTeam.CounterTerrorist)
        {
            _activeMatch.Team2.Name = name;
            _isCtNameCustom = !string.Equals(name, GetDefaultTeamName(CsTeam.CounterTerrorist), StringComparison.OrdinalIgnoreCase);
            Server.ExecuteCommand($"mp_teamname_1 \"{name}\"");
        }
    }

    private void ClearSideOwnerAndResetName(CsTeam side)
    {
        if (side == CsTeam.Terrorist)
        {
            _trNamerSteamId = 0;
            ApplySideName(side, GetDefaultTeamName(side));
        }
        else if (side == CsTeam.CounterTerrorist)
        {
            _ctNamerSteamId = 0;
            ApplySideName(side, GetDefaultTeamName(side));
        }
    }

    private void ApplyTeamOwnerOnSide(ulong steamId, CsTeam side, string ownedTeamName)
    {
        if (side == CsTeam.Terrorist)
        {
            _trNamerSteamId = steamId;
            ApplySideName(side, ownedTeamName);
        }
        else if (side == CsTeam.CounterTerrorist)
        {
            _ctNamerSteamId = steamId;
            ApplySideName(side, ownedTeamName);
        }
    }

    private void HandleTeamOwnerMove(CCSPlayerController player, CsTeam oldTeam, CsTeam newTeam)
    {
        var steamId = player.SteamID;

        if (_trNamerSteamId == steamId && oldTeam == CsTeam.Terrorist && newTeam != CsTeam.Terrorist)
        {
            ClearSideOwnerAndResetName(CsTeam.Terrorist);
        }

        if (_ctNamerSteamId == steamId && oldTeam == CsTeam.CounterTerrorist && newTeam != CsTeam.CounterTerrorist)
        {
            ClearSideOwnerAndResetName(CsTeam.CounterTerrorist);
        }

        if (!TryGetOwnedTeamName(steamId, out var ownedTeamName))
        {
            return;
        }

        if (newTeam == CsTeam.Terrorist)
        {
            if (_ctNamerSteamId == steamId)
            {
                ClearSideOwnerAndResetName(CsTeam.CounterTerrorist);
            }

            ApplyTeamOwnerOnSide(steamId, CsTeam.Terrorist, ownedTeamName);
        }
        else if (newTeam == CsTeam.CounterTerrorist)
        {
            if (_trNamerSteamId == steamId)
            {
                ClearSideOwnerAndResetName(CsTeam.Terrorist);
            }

            ApplyTeamOwnerOnSide(steamId, CsTeam.CounterTerrorist, ownedTeamName);
        }
    }

    
    [GameEventHandler]
    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (_isMatchLive || @event.Userid == null || !@event.Userid.IsValid || _activeMatch == null)
        {
            return HookResult.Continue;
        }

        var player = @event.Userid;
        var playerTeam = (CsTeam)player.TeamNum;

        if (playerTeam != CsTeam.Terrorist && playerTeam != CsTeam.CounterTerrorist)
        {
            return HookResult.Continue;
        }

        if (playerTeam == CsTeam.Terrorist && !_isTrNameCustom && string.IsNullOrEmpty(_activeMatch.Team1.Name))
        {
            _activeMatch.Team1.Name = $"time_{player.PlayerName}";
        }
        else if (playerTeam == CsTeam.CounterTerrorist && !_isCtNameCustom && string.IsNullOrEmpty(_activeMatch.Team2.Name))
        {
            _activeMatch.Team2.Name = $"time_{player.PlayerName}";
        }
        return HookResult.Continue;
    }
    public HookResult OnPlayerTeamChange(EventPlayerTeam @event, GameEventInfo info)
    {
        if (_isTeamChangeLocked)
        {
            @event.Userid?.PrintToChat($"{ChatPrefix} Você não pode trocar de time após o início da partida.");
            return HookResult.Stop;
        }

        if (@event.Userid == null || !@event.Userid.IsValid || @event.Userid.IsBot) return HookResult.Continue;
        if (_activeMatch == null) return HookResult.Continue;

        var player = @event.Userid;
        var newTeam = (CsTeam)@event.Team;
        var oldTeam = (CsTeam)@event.Oldteam;

        if (oldTeam == CsTeam.Terrorist)
            _activeMatch.Team1.Players.Remove(player.SteamID.ToString());
        else if (oldTeam == CsTeam.CounterTerrorist)
            _activeMatch.Team2.Players.Remove(player.SteamID.ToString());

        if (newTeam == CsTeam.Terrorist)
            _activeMatch.Team1.Players[player.SteamID.ToString()] = player.PlayerName;
        else if (newTeam == CsTeam.CounterTerrorist)
            _activeMatch.Team2.Players[player.SteamID.ToString()] = player.PlayerName;

        HandleTeamOwnerMove(player, oldTeam, newTeam);


        if (_isMatchLive) return HookResult.Continue;

        if (newTeam != oldTeam && (newTeam == CsTeam.Terrorist || newTeam == CsTeam.CounterTerrorist))
        {
            var playersInNewTeam = Utilities.GetPlayers().Count(p => p.IsValid && !p.IsBot && p.TeamNum == (byte)newTeam && p.SteamID != player.SteamID);
            if (playersInNewTeam == 0)
            {
                if (newTeam == CsTeam.Terrorist && !_isTrNameCustom && _trNamerSteamId == 0)
                {
                    var newName = $"time_{player.PlayerName}";
                    _activeMatch.Team1.Name = newName;
                    _trNamerSteamId = player.SteamID;
                    Server.ExecuteCommand($"mp_teamname_2 \"{newName}\"");
                    Server.PrintToChatAll($"{ChatPrefix} Nome do time Terrorista definido para {ChatColors.Green}{newName}");
                }
                else if (newTeam == CsTeam.CounterTerrorist && !_isCtNameCustom && _ctNamerSteamId == 0)
                {
                    var newName = $"time_{player.PlayerName}";
                    _activeMatch.Team2.Name = newName;
                    _ctNamerSteamId = player.SteamID;
                    Server.ExecuteCommand($"mp_teamname_1 \"{newName}\"");
                    Server.PrintToChatAll($"{ChatPrefix} Nome do time Contra-Terrorista definido para {ChatColors.Green}{newName}");
                }
            }
        }
        return HookResult.Continue;
    }
    private void StartMatch()
    {
        if (_isMatchLive) return;
        _isMatchLive = true;
        _isTeamChangeLocked = true;

        var ffValue = _pluginConfig.FogoAmigo ? 1 : 0;
        Server.ExecuteCommand($"mp_friendlyfire {ffValue}");
        
        Server.ExecuteCommand("mp_warmup_end");
        Server.ExecuteCommand("exec MatchKS/live.cfg");
        Server.PrintToChatAll($"{ChatPrefix} A partida está {ChatColors.Green}AO VIVO{ChatColors.Default}!");
        AddTimer(1.0f, () => Server.ExecuteCommand("mp_restartgame 1"));
        _isDemoStartPending = true;
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
        _isTeamChangeLocked = true;
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
        Server.PrintToChatAll($"{ChatPrefix} Placar em {ChatColors.Gold}1x0{ChatColors.Default}. Partida pausada para escolha de lado.");
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
            _isSideSwapPending = true;
            Server.ExecuteCommand("mp_swapteams");
            Server.PrintToChatAll($"{ChatPrefix} Os times trocaram de lado!");
        }

        var team1SideName = "TR";
        var team2SideName = "CT";

        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Gold}{_activeMatch.Team1.Name}{ChatColors.Default} começará como {ChatColors.Gold}{team1SideName}{ChatColors.Default}.");
        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.LightBlue}{_activeMatch.Team2.Name}{ChatColors.Default} começará como {ChatColors.LightBlue}{team2SideName}{ChatColors.Default}.");

        AddTimer(1.5f, () => {
            Server.ExecuteCommand("mp_unpause_match");
            UpdateTeamNames();
            StartMatch();
        });
    }

    [GameEventHandler]
    public HookResult OnTeamSwap(EventCsWinPanelRound @event, GameEventInfo info)
    {
        if (_activeMatch == null || !_isSideSwapPending)
            return HookResult.Continue;

        (_activeMatch.Team1, _activeMatch.Team2) = (_activeMatch.Team2, _activeMatch.Team1);
        _isSideSwapPending = false;
        AddTimer(0.5f, UpdateTeamNames);
        return HookResult.Continue;
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
        _knifeRoundWinnerTeam = null; _team1TacPausesUsed = 0; _team2TacPausesUsed = 0; _isTeamChangeLocked = false;
        _ctNamerSteamId = 0; _trNamerSteamId = 0; _isCtNameCustom = false; _isTrNameCustom = false;
        _isRestorePauseActive = false; _isRestoreReadyT = false; _isRestoreReadyCT = false;
        _isSidePickPhase = false;
        _isSideSwapPending = false;
        _sidePickCountdown = 0;
        _sidePickTimer?.Kill();
        _sidePickDisplayTimer?.Kill();
    }
    private void UpdateTeamNames()
    {
        if (_activeMatch == null) return;
        Server.ExecuteCommand($"mp_teamname_1 \"{_activeMatch.Team2.Name}\"; mp_teamlogo_1 \"{_activeMatch.Team2.Tag}\"");
        Server.ExecuteCommand($"mp_teamname_2 \"{_activeMatch.Team1.Name}\"; mp_teamlogo_2 \"{_activeMatch.Team1.Tag}\"");
    }

    private void CheckCompetitiveMode()
    {
        var gameType = ConVar.Find("game_type"); var gameMode = ConVar.Find("game_mode");
        if (gameType == null || gameMode == null || gameType.GetPrimitiveValue<int>() != 0 || gameMode.GetPrimitiveValue<int>() != 1) { Server.PrintToChatAll($"{MatchKS.ChatPrefix} O jogo não está no modo competitivo!"); }
    }
}

