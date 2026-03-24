using CounterStrikeSharp.API;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MatchKS;

public partial class MatchKS
{
    private readonly HttpClient _httpClient = new();

    private async Task SendMapStatsToDiscordAsync(List<PlayerStatsRow> rows, int roundsPlayed, string trName, int trScore, string ctName, int ctScore)
    {
        if (!_pluginConfig.DiscordWebhookEnabled) return;
        if (string.IsNullOrWhiteSpace(_pluginConfig.DiscordWebhookUrl)) return;

        try
        {
            var lines = new List<string>
            {
                $"Mapa: {Server.MapName}",
                $"Placar Final: {trName} ({trScore}) vs {ctName} ({ctScore})",
                $"Rounds: {roundsPlayed}",
                string.Empty,
                "Team | Player | K | D | K/D | K/R | ADR"
            };

            foreach (var row in rows)
            {
                lines.Add($"{row.TeamName} | {row.PlayerName} | {row.Kills} | {row.Deaths} | {row.Kd:F2} | {row.Kr:F2} | {row.Adr:F2}");
            }

            var chunks = ChunkLinesForDiscord(lines, 1700);
            foreach (var chunk in chunks)
            {
                var content = $"```\n{chunk}\n```";
                await SendDiscordMessageAsync(content);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MatchKS ERROR] Falha ao enviar stats para Discord: {ex.Message}");
        }
    }

    private static List<string> ChunkLinesForDiscord(List<string> lines, int maxChunkLength)
    {
        var chunks = new List<string>();
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            var candidate = sb.Length == 0 ? line : sb + "\n" + line;
            if (candidate.Length > maxChunkLength)
            {
                if (sb.Length > 0)
                {
                    chunks.Add(sb.ToString());
                    sb.Clear();
                }

                if (line.Length > maxChunkLength)
                {
                    chunks.Add(line.Substring(0, maxChunkLength));
                }
                else
                {
                    sb.Append(line);
                }
            }
            else
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
            }
        }

        if (sb.Length > 0)
        {
            chunks.Add(sb.ToString());
        }

        return chunks;
    }

    private async Task SendDiscordMessageAsync(string message)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["content"] = message });
        using var httpContent = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(_pluginConfig.DiscordWebhookUrl, httpContent);

        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning($"[MatchKS] Webhook Discord retornou status {(int)response.StatusCode}.");
        }
    }
}