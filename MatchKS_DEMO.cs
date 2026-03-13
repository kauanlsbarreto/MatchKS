using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.IO;

namespace MatchKS
{
    public partial class MatchKS
    {
        public void StartDemoRecording()
        {
            if (_activeMatch == null) return;
            if (!string.IsNullOrEmpty(_currentDemoName)) return;

            var demoDirectory = Path.Combine(Server.GameDirectory, "csgo", _pluginConfig.DemoFolderPath);
            if (!Directory.Exists(demoDirectory))
            {
                Directory.CreateDirectory(demoDirectory);
            }

            var format = _pluginConfig.DemoNameFormat;
            var time = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
            var matchId = DateTime.Now.Ticks.ToString();
            var map = Server.MapName;
            var team1 = SanitizeFileName(_activeMatch.Team1.Name);
            var team2 = SanitizeFileName(_activeMatch.Team2.Name);

            var demoName = format
                .Replace("{TIME}", time)
                .Replace("{MATCH_ID}", matchId)
                .Replace("{MAP}", map)
                .Replace("{TEAM1}", team1)
                .Replace("{TEAM2}", team2);

            _currentDemoName = demoName;

            var relativePath = Path.Combine(_pluginConfig.DemoFolderPath, $"{_currentDemoName}.dem").Replace('\\', '/');

            Server.PrintToChatAll($"{ChatPrefix} Iniciando gravação da demo: {ChatColors.Green}{_currentDemoName}.dem");
            Server.ExecuteCommand($"tv_record \"{relativePath}\"");
        }

        public void StopDemoRecording()
        {
            if (_currentDemoName == null) return;

            Server.ExecuteCommand("tv_stoprecord");

            var relativePath = Path.Combine(_pluginConfig.DemoFolderPath, $"{_currentDemoName}.dem").Replace('\\', '/');

            Server.PrintToChatAll($"{ChatPrefix} Gravação do mapa {ChatColors.Green}{Server.MapName}{ChatColors.Default} concluída.");
            Server.PrintToChatAll($"{ChatPrefix} Arquivo salvo em: {ChatColors.Green}{relativePath}");

            _currentDemoName = null;
        }
    }
}
