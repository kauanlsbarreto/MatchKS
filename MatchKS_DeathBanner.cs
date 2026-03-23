using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;

namespace MatchKS;

public partial class MatchKS
{
    private const float DeathBannerDurationSeconds = 3.0f;
    private const float DeathBannerRefreshSeconds = 0.20f;
    private const string DeathBannerText = "Voce ta jogando na Querido Draft";

    private readonly Dictionary<ulong, Timer> _deathBannerHideTimers = new();
    private readonly Dictionary<ulong, Timer> _deathBannerRefreshTimers = new();

    [GameEventHandler]
    public HookResult OnPlayerDeathShowBanner(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        if (victim == null || !victim.IsValid || victim.IsBot)
        {
            return HookResult.Continue;
        }

        if (victim.TeamNum != (byte)CsTeam.Terrorist && victim.TeamNum != (byte)CsTeam.CounterTerrorist)
        {
            return HookResult.Continue;
        }

        ShowDeathBanner(victim);
        return HookResult.Continue;
    }

    private void ShowDeathBanner(CCSPlayerController player)
    {
        if (!player.IsValid || player.SteamID == 0)
        {
            return;
        }

        if (_deathBannerHideTimers.TryGetValue(player.SteamID, out var existingHide))
        {
            existingHide.Kill();
            _deathBannerHideTimers.Remove(player.SteamID);
        }

        if (_deathBannerRefreshTimers.TryGetValue(player.SteamID, out var existingRefresh))
        {
            existingRefresh.Kill();
            _deathBannerRefreshTimers.Remove(player.SteamID);
        }

        var html = "<br><br><br><br><br>" +
                   $"<font color='orange'>{DeathBannerText}</font>";

        var expiresAt = DateTime.UtcNow.AddSeconds(DeathBannerDurationSeconds);
        player.PrintToCenterHtml(html);

        _deathBannerRefreshTimers[player.SteamID] = AddTimer(DeathBannerRefreshSeconds, () =>
        {
            if (!player.IsValid || DateTime.UtcNow >= expiresAt)
            {
                _deathBannerRefreshTimers.Remove(player.SteamID);
                return;
            }
            player.PrintToCenterHtml(html);
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

        _deathBannerHideTimers[player.SteamID] = AddTimer(DeathBannerDurationSeconds, () =>
        {
            if (_deathBannerRefreshTimers.TryGetValue(player.SteamID, out var rt))
            {
                rt.Kill();
                _deathBannerRefreshTimers.Remove(player.SteamID);
            }

            if (player.IsValid)
            {
                player.PrintToCenterHtml(" ");
            }

            _deathBannerHideTimers.Remove(player.SteamID);
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }
}
