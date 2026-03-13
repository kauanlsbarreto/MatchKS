using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Generic;
using System.Linq;

namespace MatchKS;


public class DamagePlayerInfo
{
    public int DamageDealt { get; set; } = 0;
    public int Hits { get; set; } = 0;
}

public partial class MatchKS
{

    private readonly Dictionary<int, Dictionary<int, DamagePlayerInfo>> _playerDamageInfo = new();
    private readonly Dictionary<ulong, int> _mapDamageByPlayer = new();

    [GameEventHandler]
    public HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!_isMatchLive || _isKnifeRoundActive) return HookResult.Continue;
        
        var attacker = @event.Attacker;
        var victim = @event.Userid;

        if (attacker == null || victim == null || !attacker.IsValid || !victim.IsValid || attacker.UserId == victim.UserId || attacker.TeamNum == victim.TeamNum)
        {
            return HookResult.Continue;
        }

        var attackerId = attacker.UserId ?? 0;
        var victimId = victim.UserId ?? 0;

        if (!_playerDamageInfo.TryGetValue(attackerId, out var attackerDamageDict))
        {
            attackerDamageDict = new Dictionary<int, DamagePlayerInfo>();
            _playerDamageInfo[attackerId] = attackerDamageDict;
        }
        if (!attackerDamageDict.TryGetValue(victimId, out var damageInfo))
        {
            damageInfo = new DamagePlayerInfo();
            attackerDamageDict[victimId] = damageInfo;
        }

        damageInfo.DamageDealt += @event.DmgHealth;
        damageInfo.Hits++;

        if (attacker != null && attacker.SteamID != 0)
        {
            _mapDamageByPlayer[attacker.SteamID] = _mapDamageByPlayer.GetValueOrDefault(attacker.SteamID) + @event.DmgHealth;
        }

        return HookResult.Continue;
    }


    [GameEventHandler]
    public HookResult OnRoundEnd_DamageReport(EventRoundEnd @event, GameEventInfo info)
    {
        if (!_isMatchLive || _isKnifeRoundActive)
        {
            return HookResult.Continue;
        }

        Server.NextFrame(() =>
        {
            ShowDamageReport();
            _playerDamageInfo.Clear();
        });

        return HookResult.Continue;
    }
    private void ShowDamageReport()
    {
        var allPlayersAndBots = Utilities.GetPlayers().Where(p => p.IsValid && p.TeamNum > 1).ToList();
        var humanPlayers = allPlayersAndBots.Where(p => !p.IsBot).ToList();

        foreach (var player in humanPlayers)
        {
            player.PrintToChat($"{ChatPrefix} Relatório de Dano do Round:");
            var playerId = player.UserId ?? 0;

            foreach (var enemy in allPlayersAndBots)
            {
                if (enemy.UserId == playerId || enemy.TeamNum == player.TeamNum)
                {
                    continue;
                }

                var enemyId = enemy.UserId ?? 0;
                
                int enemyHealth = (enemy.PawnIsAlive && enemy.PlayerPawn?.Value != null) ? enemy.PlayerPawn.Value.Health : 0;
                if (enemyHealth < 0) enemyHealth = 0;

                string reportLine = $" {ChatColors.Default}- [{ChatColors.Green}{enemyHealth} HP{ChatColors.Default}] {ChatColors.Lime}{enemy.PlayerName}";
                
                if (_playerDamageInfo.TryGetValue(playerId, out var victimDict) && victimDict.TryGetValue(enemyId, out var damageInfo))
                {
                    if (damageInfo.DamageDealt > 0)
                    {
                        reportLine += $" {ChatColors.Default}= [{ChatColors.Red}{damageInfo.DamageDealt} HP{ChatColors.Default}]";
                    }
                }
                
                player.PrintToChat(reportLine);
            }
        }
    }
}
