using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using System.Linq;

namespace MatchKS;

public partial class MatchKS
{
    private bool _teamsSideSwapped = false;

    [GameEventHandler]
    public HookResult OnPlayerTeam_AutoName(EventPlayerTeam @event, GameEventInfo info)
    {
        if (_activeMatch == null) return HookResult.Continue;
        
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot || player.SteamID == 0) 
            return HookResult.Continue;

        if (_isMatchLive || _isSwitchingSides) return HookResult.Continue;

        var newTeam = (CsTeam)@event.Team;
        var oldTeam = (CsTeam)@event.Oldteam;

        if (oldTeam == CsTeam.Terrorist || oldTeam == CsTeam.CounterTerrorist)
        {
            MatchTeam? oldMatchTeam = (oldTeam == CsTeam.Terrorist) ? _activeMatch.Team1 : _activeMatch.Team2;
            
            if (oldMatchTeam.CaptainSteamId == player.SteamID)
            {
                oldMatchTeam.CaptainSteamId = 0;
                oldMatchTeam.Name = oldTeam == CsTeam.Terrorist ? "Terroristas" : "Contra-Terroristas";
                Server.PrintToChatAll($"{ChatPrefix} O nome do time {GetTeamName((byte)oldTeam)} foi resetado (capitão mudou de time).");
                UpdateTeamNamesCvars();
            }
            else
            {
                if (CheckAndResetEmptyTeamOwner(oldMatchTeam, oldTeam))
                    UpdateTeamNamesCvars();
            }
        }

        MatchTeam? targetMatchTeam = null;
        if (newTeam == CsTeam.Terrorist) targetMatchTeam = _activeMatch.Team1;
        else if (newTeam == CsTeam.CounterTerrorist) targetMatchTeam = _activeMatch.Team2;

        if (targetMatchTeam != null)
        {
            if (targetMatchTeam.CaptainSteamId == 0)
            {
                targetMatchTeam.CaptainSteamId = player.SteamID;
                targetMatchTeam.Name = $"time_{player.PlayerName}";
                
                Server.PrintToChatAll($"{ChatPrefix} O time {GetTeamName((byte)newTeam)} agora se chama {ChatColors.Green}{targetMatchTeam.Name}{ChatColors.Default}.");
                UpdateTeamNamesCvars();
            }
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDisconnect_AutoName(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (_activeMatch == null || _isMatchLive) return HookResult.Continue;

        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;
        
        Server.NextFrame(() => {
            if (_activeMatch == null) return;
            
            bool changedT = HandleDisconnectOwner(_activeMatch.Team1, CsTeam.Terrorist, player.SteamID);
            bool changedCT = HandleDisconnectOwner(_activeMatch.Team2, CsTeam.CounterTerrorist, player.SteamID);
            
            if (changedT || changedCT) UpdateTeamNamesCvars();
        });

        return HookResult.Continue;
    }

    private bool HandleDisconnectOwner(MatchTeam team, CsTeam side, ulong disconnectedSteamId)
    {
        if (team.CaptainSteamId == disconnectedSteamId)
        {
            team.CaptainSteamId = 0;
            team.Name = side == CsTeam.Terrorist ? "Terroristas" : "Contra-Terroristas";
            return true;
        }
        return CheckAndResetEmptyTeamOwner(team, side);
    }

    private bool CheckAndResetEmptyTeamOwner(MatchTeam team, CsTeam side)
    {
        if (team.CaptainSteamId == 0) return false;

        int playersInTeam = Utilities.GetPlayers().Count(p => 
            p.IsValid && !p.IsBot && p.Connected == PlayerConnectedState.PlayerConnected && p.TeamNum == (byte)side);

        if (playersInTeam == 0)
        {
            team.CaptainSteamId = 0;
            team.Name = side == CsTeam.Terrorist ? "Terroristas" : "Contra-Terroristas";
            return true;
        }
        return false;
    }

    private void UpdateTeamNamesCvars()
    {
        if (_activeMatch == null) return;
        
        Server.ExecuteCommand($"mp_teamname_1 \"{_activeMatch.Team2.Name}\"");
        Server.ExecuteCommand($"mp_teamname_2 \"{_activeMatch.Team1.Name}\"");
    }
}