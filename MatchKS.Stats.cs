using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MatchKS;

public partial class MatchKS
{
    private sealed class PlayerStatsRow
    {
        public string TeamName { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public string PlayerId { get; set; } = string.Empty;
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public double Kd { get; set; }
        public double Kr { get; set; }
        public double Adr { get; set; }
    }

    private string SanitizeFileName(string name)
    {
        string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
        var regex = new Regex($"[{Regex.Escape(invalidChars)}]");
        return regex.Replace(name, "");
    }

    private void WriteMatchSummaryFile(string? reason)
    {
        if (_activeMatch == null) return;

        try
        {
            if (!Directory.Exists(MatchSummaryFolderPath))
            {
                Directory.CreateDirectory(MatchSummaryFolderPath);
            }

            var teamEntities = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            var teamCt = teamEntities.FirstOrDefault(t => t.TeamNum == (byte)CsTeam.CounterTerrorist);
            var teamTr = teamEntities.FirstOrDefault(t => t.TeamNum == (byte)CsTeam.Terrorist);

            int trScore = teamTr?.Score ?? 0;
            int ctScore = teamCt?.Score ?? 0;
            int team1Score = _activeMatch.Team1.Name == GetTeamName((byte)CsTeam.Terrorist) ? trScore : ctScore;
            int team2Score = _activeMatch.Team2.Name == GetTeamName((byte)CsTeam.CounterTerrorist) ? ctScore : trScore;

            var fileName = $"match_end_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{SanitizeFileName(Server.MapName)}.txt";
            var fullPath = Path.Combine(MatchSummaryFolderPath, fileName);

            var lines = new List<string>
            {
                $"Data={DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"Motivo={(string.IsNullOrWhiteSpace(reason) ? "Nao informado" : reason)}",
                $"Mapa={Server.MapName}",
                $"Team1={_activeMatch.Team1.Name}",
                $"Team2={_activeMatch.Team2.Name}",
                $"ScoreTeam1={team1Score}",
                $"ScoreTeam2={team2Score}",
            };

            File.WriteAllLines(fullPath, lines);
            Logger.LogInformation($"[MatchKS] Resumo final da partida salvo em: {fullPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MatchKS ERROR] Falha ao salvar resumo final da partida: {ex.Message}");
        }
    }

    private void WritePerMapPlayerStatsFile()
    {
        if (_activeMatch == null) return;

        try
        {
            if (!Directory.Exists(MatchSummaryFolderPath))
            {
                Directory.CreateDirectory(MatchSummaryFolderPath);
            }

            var teamEntities = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            var teamCt = teamEntities.FirstOrDefault(t => t.TeamNum == (byte)CsTeam.CounterTerrorist);
            var teamTr = teamEntities.FirstOrDefault(t => t.TeamNum == (byte)CsTeam.Terrorist);
            int roundsPlayed = Math.Max(1, (teamCt?.Score ?? 0) + (teamTr?.Score ?? 0));

            var rows = new List<PlayerStatsRow>();
            var players = Utilities.GetPlayers()
                .Where(p => p.IsValid && (p.TeamNum == (byte)CsTeam.Terrorist || p.TeamNum == (byte)CsTeam.CounterTerrorist))
                .ToList();

            foreach (var player in players)
            {
                int kills = GetPlayerKills(player);
                int deaths = GetPlayerDeaths(player);
                int damage = _mapDamageByPlayer.GetValueOrDefault(player.SteamID);

                rows.Add(new PlayerStatsRow
                {
                    TeamName = GetTeamName(player.TeamNum),
                    PlayerName = player.PlayerName,
                    PlayerId = player.IsBot ? $"BOT_{player.UserId}" : player.SteamID.ToString(),
                    Kills = kills,
                    Deaths = deaths,
                    Kd = deaths > 0 ? (double)kills / deaths : kills,
                    Kr = (double)kills / roundsPlayed,
                    Adr = (double)damage / roundsPlayed
                });
            }

            rows = rows
                .OrderBy(r => r.TeamName)
                .ThenByDescending(r => r.Kills)
                .ThenBy(r => r.Deaths)
                .ToList();

            var fileName = $"map_end_stats_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{SanitizeFileName(Server.MapName)}.txt";
            var fullPath = Path.Combine(MatchSummaryFolderPath, fileName);

            var output = new List<string>
            {
                $"Data={DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"Mapa={Server.MapName}",
                $"Rounds={roundsPlayed}",
                $"TeamTR={GetTeamName((byte)CsTeam.Terrorist)}",
                $"TeamCT={GetTeamName((byte)CsTeam.CounterTerrorist)}",
                string.Empty,
                "Team;Player;ID;Kills;Deaths;K/D;K/R;ADR"
            };

            foreach (var row in rows)
            {
                output.Add($"{row.TeamName};{row.PlayerName};{row.PlayerId};{row.Kills};{row.Deaths};{row.Kd:F2};{row.Kr:F2};{row.Adr:F2}");
            }

            File.WriteAllLines(fullPath, output);
            Logger.LogInformation($"[MatchKS] Stats do mapa salvos em: {fullPath}");
            _ = SendMapStatsToDiscordAsync(rows, roundsPlayed);
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MatchKS ERROR] Falha ao salvar stats por mapa: {ex.Message}");
        }
    }

    private int GetPlayerKills(CCSPlayerController player)
    {
        // Estilo MatchZy: Acesso direto a propriedade ActionTrackingServices
        return player.ActionTrackingServices?.MatchStats?.Kills ?? 0;
    }

    private int GetPlayerDeaths(CCSPlayerController player)
    {
        // Estilo MatchZy: Acesso direto
        return player.ActionTrackingServices?.MatchStats?.Deaths ?? 0;
    }
}