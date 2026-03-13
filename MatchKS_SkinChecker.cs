using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MatchKS;

public partial class MatchKS
{
    private static readonly string[] CtModelWhitelist =
    {
        "characters/models/ctm_fbi/",
        "characters/models/ctm_gign/",
        "characters/models/ctm_gsg9/",
        "characters/models/ctm_idf/",
        "characters/models/ctm_sas/",
        "characters/models/ctm_st6/",
        "characters/models/ctm_swat/"
    };

    private static readonly string[] TModelWhitelist =
    {
        "characters/models/tm_anarchist/",
        "characters/models/tm_balkan/",
        "characters/models/tm_elite_crew/",
        "characters/models/tm_leet/",
        "characters/models/tm_phoenix/",
        "characters/models/tm_pirate/",
        "characters/models/tm_professional/",
        "characters/models/tm_separatist/"
    };

    [GameEventHandler]
    public HookResult OnPlayerSpawn_SkinCheck(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (!_isSkinCheckerEnabled)
            return HookResult.Continue;

        var player = @event.Userid;

        if (player == null || !player.IsValid || player.IsBot || player.PlayerPawn?.Value == null)
            return HookResult.Continue;

        _ = AddTimer(0.5f, () =>
        {
            if (player == null || !player.IsValid || player.PlayerPawn?.Value == null)
                return;

            var playerModel = player.PlayerPawn.Value.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName;
            if (string.IsNullOrWhiteSpace(playerModel))
                return;

            var normalizedModel = playerModel.Replace('\\', '/');
            var playerTeam = (CsTeam)player.TeamNum;
            bool isAllowed = false;

            if (playerTeam == CsTeam.CounterTerrorist)
            {
                isAllowed = CtModelWhitelist.Any(prefix => normalizedModel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }
            else if (playerTeam == CsTeam.Terrorist)
            {
                isAllowed = TModelWhitelist.Any(prefix => normalizedModel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                isAllowed = true;
            }

            if (isAllowed)
                return;

            if (!player.UserId.HasValue)
                return;

            player.PrintToChat(
                $"{ChatPrefix} {ChatColors.Red}Skin não padrão detectada{ChatColors.Default}: troque para uma skin padrão do CS2 para jogar."
            );

            Server.ExecuteCommand(
                $"kickid {player.UserId.Value} \"Sua skin de personagem não é padrão. Por favor, troque para uma skin padrão do CS2 para jogar.\""
            );
        });

        return HookResult.Continue;
    }
}
