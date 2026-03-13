using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Events;
using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;

namespace MatchKS;

public partial class MatchKS : BasePlugin

{
    public override string ModuleName => "MatchKS Plugin";
    public override string ModuleVersion => "1.0.5";
    public override string ModuleAuthor => "Kauan";
    public static string ChatPrefixText = "MatchKS";
    public static string ChatPrefixColor = ChatColors.Blue.ToString();
    public static string ChatPrefix => $" {ChatPrefixColor}[{ChatPrefixText}]{ChatColors.Default}";

    public static MatchConfig? _activeMatch;
    public static PluginConfig _pluginConfig = new();
    private string MatchesConfigPath => Server.GameDirectory;
    private string ActiveMatchFilePath => Path.Combine(MatchesConfigPath, "active_match.json");
    private string PluginConfigPath => Path.Combine(FeseeCfgFolderPath, "config.cfg");
    private string TeamNameOwnerConfigPath => Path.Combine(FeseeCfgFolderPath, "team_name_owner.json");
    private string MatchSummaryFolderPath => Path.Combine(FeseeCfgFolderPath, "history");
    private string FeseeCfgFolderPath => Path.Combine(Server.GameDirectory, "csgo", "cfg", "MatchKS");

    public static string BackupFolderPath { get; private set; } = "";

    private string _currentRoundBackupFile = "";

    private string? _currentDemoName = null;
    private bool _isDemoStartPending = false;


    private bool _isTeamTReady, _isTeamCTReady, _isMatchLive, _isKnifeRoundEnabledForCurrentMap, _isKnifeRoundActive;
    private bool _isTeamChangeLocked = false;
    private CsTeam? _knifeRoundWinnerTeam;
    private int _team1TacPausesUsed, _team2TacPausesUsed;
    private int _currentRoundNumber = 0;
    private bool _isSkinCheckerEnabled = false;

    private bool _isPauseActive;
    private CsTeam? _pausingTeam;
    private bool _isPauseScheduled = false;
    private CsTeam _pausingTeamScheduled = CsTeam.None;
    private Timer? _tacTimer;
    private Timer? _pauseDisplayTimer;
    private int _pauseCountdown;

    private bool _isTechPauseActive = false;
    private bool _isTechPauseScheduled = false;

    private bool _isRestorePauseActive = false;
    private bool _isRestoreReadyT = false;
    private bool _isRestoreReadyCT = false;


    private ulong _ctNamerSteamId;
    private ulong _trNamerSteamId;
    private bool _isCtNameCustom;
    private bool _isTrNameCustom;
    private TeamNameOwnerConfig _teamNameOwnerConfig = new();

    private bool _mapLogicHasRun = false;
    private Timer? _competitiveCheckTimer, _readyStatusTimer;

    private bool _gotvSettingsApplied = false;

    private bool _isSidePickPhase = false;
    private bool _isSideSwapPending = false;
    private Timer? _sidePickTimer;
    private Timer? _sidePickDisplayTimer;
    private int _sidePickCountdown = 0;
    private readonly HttpClient _httpClient = new();

    public int PausesTaticoPorEquipe { get; set; } = 2;
    public int DuracaoPauseTatico { get; set; } = 60;
    public bool RoundFaca { get; set; } = true;
    public bool FogoAmigo { get; set; } = false;

    public override void Load(bool hotReload)
    {
        Logger.LogInformation($"[MatchKS DEBUG] Carregando Plugin v{ModuleVersion}...");
        AddTimer(10.0f, SyncTvDelayAcrossConfigs);

        BackupFolderPath = Path.Join(Server.GameDirectory, "csgo", "BackupMatchKS");
        if (!Directory.Exists(BackupFolderPath))
        {
            Directory.CreateDirectory(BackupFolderPath);
            Logger.LogInformation($"[MatchKS] Diretório de backups criado em: {BackupFolderPath}");
        }

        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            _mapLogicHasRun = false;
            _gotvSettingsApplied = false;
            AddTimer(1.0f, RunMapStartLogic);
            AddTimer(5.0f, ApplyGotvSettings);
        });

        RegisterEventHandler<EventRoundStart>(OnRoundStartBackupHandler, HookMode.Post);

        RegisterEventHandler<EventMapShutdown>(OnMapShutdownHandler);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeamChange);
        RegisterEventHandler<EventGameEnd>(OnGameEnd);

        Logger.LogInformation("[MatchKS DEBUG] Eventos do jogo registrados com sucesso.");

        if (!Directory.Exists(FeseeCfgFolderPath)) { Directory.CreateDirectory(FeseeCfgFolderPath); }
        LoadConfigFromCfg();
        LoadTeamNameOwnerConfig();

        if (File.Exists(ActiveMatchFilePath))
        {
            try
            {
                _activeMatch = JsonSerializer.Deserialize<MatchConfig>(File.ReadAllText(ActiveMatchFilePath));
                Logger.LogInformation("[MatchKS DEBUG] Partida de campeonato ativa carregada do arquivo de estado.");
            }
            catch (Exception e) { Logger.LogError($"[MatchKS ERROR] Falha ao carregar partida ativa: {e.Message}"); }
        }

        Logger.LogInformation("[MatchKS DEBUG] Plugin CARREGADO com sucesso.");
    }

    private void LoadConfigFromCfg()
    {
        if (!File.Exists(PluginConfigPath))
        {
            var defaultConfigLines = new List<string>
            {
                $"PausesTaticoPorEquipe={_pluginConfig.PausesTaticoPorEquipe}",
                $"DuracaoPauseTatico={_pluginConfig.DuracaoPauseTatico}",
                $"RoundFaca={(_pluginConfig.RoundFaca ? "true" : "false")}",
                $"FogoAmigo={(_pluginConfig.FogoAmigo ? "true" : "false")}",
                $"ChatPrefixText=\"{ChatPrefixText}\"",
                "ChatPrefixColor=\"blue\"",
                "nome_formato_demo=\"{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}\"",
                "demo_pasta=\"matchksDEMOS/\"",
                $"discord_webhook_enabled={(_pluginConfig.DiscordWebhookEnabled ? "true" : "false")}",
                $"discord_webhook_url=\"{_pluginConfig.DiscordWebhookUrl}\""
            };
            File.WriteAllLines(PluginConfigPath, defaultConfigLines);
            Logger.LogInformation($"[MatchKS] Arquivo de configuração não encontrado. Criado em: {PluginConfigPath}");
            return;
        }

        var configLines = File.ReadAllLines(PluginConfigPath);
        var configDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in configLines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("//")) continue;

            var parts = line.Split('=', 2);
            if (parts.Length == 2)
            {
                configDict[parts[0].Trim()] = parts[1].Trim().Trim('"');
            }
        }

        if (configDict.TryGetValue("PausesTaticoPorEquipe", out var pauses) && int.TryParse(pauses, out var pausesValue))
        {
            _pluginConfig.PausesTaticoPorEquipe = pausesValue;
        }
        if (configDict.TryGetValue("DuracaoPauseTatico", out var duracao) && int.TryParse(duracao, out var duracaoValue))
        {
            _pluginConfig.DuracaoPauseTatico = duracaoValue;
        }
        if (configDict.TryGetValue("RoundFaca", out var faca) && bool.TryParse(faca, out var facaValue))
        {
            _pluginConfig.RoundFaca = facaValue;
        }
        if (configDict.TryGetValue("FogoAmigo", out var ff) && bool.TryParse(ff, out var ffValue))
        {
            _pluginConfig.FogoAmigo = ffValue;
        }
        if (configDict.TryGetValue("ChatPrefixText", out var prefixText) && !string.IsNullOrWhiteSpace(prefixText))
        {
            ChatPrefixText = prefixText.Trim();
        }
        if (configDict.TryGetValue("ChatPrefixColor", out var prefixColor) && !string.IsNullOrWhiteSpace(prefixColor))
        {
            ChatPrefixColor = ResolveChatColor(prefixColor);
        }
        if (configDict.TryGetValue("nome_formato_demo", out var demoFormat))
        {
            _pluginConfig.DemoNameFormat = demoFormat;
        }
        if (configDict.TryGetValue("demo_pasta", out var demoFolder))
        {
            _pluginConfig.DemoFolderPath = demoFolder;
        }
        if (configDict.TryGetValue("discord_webhook_enabled", out var webhookEnabled) && bool.TryParse(webhookEnabled, out var webhookEnabledValue))
        {
            _pluginConfig.DiscordWebhookEnabled = webhookEnabledValue;
        }
        if (configDict.TryGetValue("discord_webhook_url", out var webhookUrl) && !string.IsNullOrWhiteSpace(webhookUrl))
        {
            _pluginConfig.DiscordWebhookUrl = webhookUrl.Trim();
        }
        Logger.LogInformation("[MatchKS] Configurações carregadas do arquivo config.cfg.");
    }

    private static string ResolveChatColor(string colorName)
    {
        return colorName.Trim().ToLowerInvariant() switch
        {
            "blue" or "azul" => ChatColors.Blue.ToString(),
            "red" or "vermelho" => ChatColors.Red.ToString(),
            "green" or "verde" => ChatColors.Green.ToString(),
            "yellow" or "amarelo" => ChatColors.Yellow.ToString(),
            "gold" => ChatColors.Gold.ToString(),
            "orange" or "laranja" => ChatColors.Orange.ToString(),
            "purple" or "roxo" => ChatColors.Purple.ToString(),
            "lime" => ChatColors.Lime.ToString(),
            "lightblue" or "azulclaro" => ChatColors.LightBlue.ToString(),
            "default" or "padrao" => ChatColors.Default.ToString(),
            _ => ChatColors.Blue.ToString()
        };
    }

    private void LoadTeamNameOwnerConfig()
    {
        try
        {
            if (!File.Exists(TeamNameOwnerConfigPath))
            {
                SaveTeamNameOwnerConfig();
                return;
            }

            _teamNameOwnerConfig = JsonSerializer.Deserialize<TeamNameOwnerConfig>(File.ReadAllText(TeamNameOwnerConfigPath))
                                  ?? new TeamNameOwnerConfig();
        }
        catch (Exception ex)
        {
            _teamNameOwnerConfig = new TeamNameOwnerConfig();
            Logger.LogError($"[MatchKS ERROR] Falha ao carregar team_name_owner.json: {ex.Message}");
        }
    }

    private void SaveTeamNameOwnerConfig()
    {
        try
        {
            File.WriteAllText(
                TeamNameOwnerConfigPath,
                JsonSerializer.Serialize(_teamNameOwnerConfig, new JsonSerializerOptions { WriteIndented = true })
            );
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MatchKS ERROR] Falha ao salvar team_name_owner.json: {ex.Message}");
        }
    }

    private string GetDefaultTeamName(CsTeam team)
    {
        return team == CsTeam.Terrorist ? "Terroristas" : "Contra-Terroristas";
    }

    private void ResetMatchState()
    {
        _isMatchLive = false;
        _isKnifeRoundActive = false;
        _isSidePickPhase = false;
        _isTeamChangeLocked = false;

        _isTeamTReady = false;
        _isTeamCTReady = false;
        _knifeRoundWinnerTeam = null;
        _team1TacPausesUsed = 0;
        _team2TacPausesUsed = 0;
        _currentRoundNumber = 0;
        _isDemoStartPending = false;

        _isPauseActive = false;
        _isTechPauseActive = false;
        _pausingTeam = null;
        _isRestorePauseActive = false;
        _isRestoreReadyT = false;
        _isRestoreReadyCT = false;

        _sidePickTimer?.Kill();
        _sidePickDisplayTimer?.Kill();
        _tacTimer?.Kill();
        _pauseDisplayTimer?.Kill();

        Logger.LogInformation("[MatchKS] O estado da partida foi reiniciado.");
    }
    private void SyncTvDelayAcrossConfigs()
    {
        string sourceCfgPath = Path.Join(Server.GameDirectory, "csgo", "cfg", "MatchKS", "gotv.cfg");
        string cfgDirectory = Path.Combine(Server.GameDirectory, "csgo", "cfg");
        string? targetDelayValue = null;

        Logger.LogInformation("[MatchKS] Iniciando sincronização do 'tv_delay'...");

        try
        {
            if (!File.Exists(sourceCfgPath))
            {
                Logger.LogError($"[MatchKS] ERRO: Arquivo de origem '{sourceCfgPath}' não encontrado. Sincronização cancelada.");
                return;
            }

            var sourceLines = File.ReadAllLines(sourceCfgPath);
            foreach (var line in sourceLines)
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("tv_delay", StringComparison.OrdinalIgnoreCase))
                {
                    targetDelayValue = trimmedLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                    break;
                }
            }

            if (string.IsNullOrEmpty(targetDelayValue))
            {
                Logger.LogWarning("[MatchKS] AVISO: 'tv_delay' não encontrado ou sem valor no arquivo de origem. Sincronização cancelada.");
                return;
            }

            Logger.LogInformation($"[MatchKS] Valor de 'tv_delay' a ser aplicado: {targetDelayValue}");

            var allCfgFiles = Directory.GetFiles(cfgDirectory, "*.cfg", SearchOption.AllDirectories);
            int filesUpdated = 0;

            foreach (var filePath in allCfgFiles)
            {
                var pathComparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

                if (Path.GetFullPath(filePath).Equals(Path.GetFullPath(sourceCfgPath), pathComparison))
                    continue;

                var lines = File.ReadAllLines(filePath).ToList();
                bool fileModified = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().StartsWith("tv_delay", StringComparison.OrdinalIgnoreCase))
                    {
                        string newLine = $"tv_delay        {targetDelayValue}";
                        if (lines[i].Trim() != newLine.Trim())
                        {
                            lines[i] = newLine;
                            fileModified = true;
                        }
                        break;
                    }
                }
                if (fileModified)
                {
                    File.WriteAllLines(filePath, lines);
                    filesUpdated++;
                }
            }
            Logger.LogInformation($"[MatchKS] Sincronização de 'tv_delay' concluída. {filesUpdated} arquivos foram atualizados.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MatchKS] ERRO durante a sincronização de 'tv_delay': {ex.Message}");
        }
    }

    private string SanitizeFileName(string name)
    {
        string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
        var regex = new Regex($"[{Regex.Escape(invalidChars)}]");
        return regex.Replace(name, "");
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (_isKnifeRoundActive)
        {
            var eventWinner = (CsTeam)@event.Winner;
            var knifeWinner = DetermineKnifeRoundWinner(eventWinner, out var decisionReason);

            if (knifeWinner != CsTeam.None)
            {
                Server.PrintToChatAll($"{ChatPrefix} Round de faca decidido por {ChatColors.Green}{decisionReason}{ChatColors.Default}.");
                HandleKnifeRoundEnd(knifeWinner);
            }
        }

        return HookResult.Continue;
    }

    private CsTeam DetermineKnifeRoundWinner(CsTeam eventWinner, out string reason)
    {
        reason = "criterio padrao";

        var knifePlayers = Utilities.GetPlayers()
            .Where(p => p.IsValid && (p.TeamNum == (byte)CsTeam.Terrorist || p.TeamNum == (byte)CsTeam.CounterTerrorist))
            .ToList();

        var tPlayers = knifePlayers.Where(p => p.TeamNum == (byte)CsTeam.Terrorist).ToList();
        var ctPlayers = knifePlayers.Where(p => p.TeamNum == (byte)CsTeam.CounterTerrorist).ToList();

        bool tEliminated = !tPlayers.Any(IsAliveWithHealth);
        bool ctEliminated = !ctPlayers.Any(IsAliveWithHealth);

        if (tEliminated ^ ctEliminated)
        {
            reason = "eliminacao total";
            return tEliminated ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        }

        int tTotalHealth = tPlayers.Where(IsAliveWithHealth).Sum(GetPlayerHealthSafe);
        int ctTotalHealth = ctPlayers.Where(IsAliveWithHealth).Sum(GetPlayerHealthSafe);

        if (tTotalHealth != ctTotalHealth)
        {
            reason = $"soma de vida restante (TR {tTotalHealth} x CT {ctTotalHealth})";
            return tTotalHealth > ctTotalHealth ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
        }

        int tAliveCount = tPlayers.Count(IsAliveWithHealth);
        int ctAliveCount = ctPlayers.Count(IsAliveWithHealth);
        if (tAliveCount != ctAliveCount)
        {
            reason = $"desempate por jogadores vivos (TR {tAliveCount} x CT {ctAliveCount})";
            return tAliveCount > ctAliveCount ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
        }

        if (eventWinner == CsTeam.Terrorist || eventWinner == CsTeam.CounterTerrorist)
        {
            reason = "criterio padrao do jogo (empate)";
            return eventWinner;
        }

        reason = "empate sem vencedor";
        return CsTeam.None;
    }

    private static bool IsAliveWithHealth(CCSPlayerController player)
    {
        return player.PawnIsAlive && player.PlayerPawn?.Value != null && player.PlayerPawn.Value.Health > 0;
    }

    private static int GetPlayerHealthSafe(CCSPlayerController player)
    {
        return player.PlayerPawn?.Value?.Health ?? 0;
    }

    public HookResult OnRoundStartBackupHandler(EventRoundStart @event, GameEventInfo info)
    {
        if (_isMatchLive && _activeMatch != null)
        {
            if (_isDemoStartPending)
            {
                StartDemoRecording();
                _isDemoStartPending = false;
            }

            var teamEntities = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            var teamCt = teamEntities.FirstOrDefault(t => t.TeamNum == (byte)CsTeam.CounterTerrorist);
            var teamTr = teamEntities.FirstOrDefault(t => t.TeamNum == (byte)CsTeam.Terrorist);

            if (teamCt == null || teamTr == null) return HookResult.Continue;

            int score1 = _activeMatch.Team1.Name == GetTeamName((byte)CsTeam.Terrorist) ? teamTr.Score : teamCt.Score;
            int score2 = _activeMatch.Team2.Name == GetTeamName((byte)CsTeam.CounterTerrorist) ? teamCt.Score : teamTr.Score;
            _currentRoundNumber = teamCt.Score + teamTr.Score;

            var team1Name = SanitizeFileName(_activeMatch.Team1.Name);
            var team2Name = SanitizeFileName(_activeMatch.Team2.Name);
            var mapName = SanitizeFileName(Server.MapName);

            // Nome deterministico evita criar novos backups para o mesmo estado apos restore.
            var fileName = $"{team1Name}_{team2Name}_{mapName}_{_currentRoundNumber}_{score1}_{score2}.txt";
            _currentRoundBackupFile = fileName;

            var relativePath = Path.Join("BackupMatchKS", fileName).Replace('\\', '/');

            Server.ExecuteCommand($"mp_backup_round_file \"{relativePath}\"");
            Logger.LogInformation($"[MatchKS] Backup de INÍCIO de round salvo em: {relativePath}");
        }
        return HookResult.Continue;
    }

    private void CreateDefaultPugMatch()
    {
        _activeMatch = new MatchConfig
        {
            Team1 = new MatchTeam { Name = "Terroristas", Tag = "TR" },
            Team2 = new MatchTeam { Name = "Contra-Terroristas", Tag = "CT" },
            MapList = new List<MapConfig> { new MapConfig { Name = Server.MapName } }
        };
    }

    public HookResult OnMapShutdownHandler(EventMapShutdown @event, GameEventInfo info)
    {
        _mapLogicHasRun = false;
        return HookResult.Continue;
    }

    public HookResult OnPlayerConnectFullHandler(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (!_mapLogicHasRun)
        {
            _mapLogicHasRun = true;
            AddTimer(1.0f, RunMapStartLogic);
        }
        return HookResult.Continue;
    }

    private void RunMapStartLogic()
    {
        Logger.LogInformation($"[MatchKS] Lógica de início de mapa executada para '{Server.MapName}'.");

        if (_activeMatch == null)
        {
            Logger.LogInformation("[MatchKS] Criando PUG padrão.");
            CreateDefaultPugMatch();
        }

        _mapDamageByPlayer.Clear();

        ResetMapStates();
        var currentMap = _activeMatch!.MapList[_activeMatch.CurrentMapIndex];
        _isKnifeRoundEnabledForCurrentMap = currentMap.EnableKnifeRound;

        Server.ExecuteCommand("exec MatchKS/warmup.cfg");
        Server.PrintToConsole("[MatchKS] Executed warmup.cfg.");

        _readyStatusTimer?.Kill();
        _competitiveCheckTimer?.Kill();

        _readyStatusTimer = AddTimer(15.0f, AnnounceReadyStatus, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        _competitiveCheckTimer = AddTimer(15.0f, CheckCompetitiveMode, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

        Server.ExecuteCommand("mp_backup_round_file_pattern ''");
        Server.ExecuteCommand("mp_backup_rules_changed_on_live 1");

        Server.ExecuteCommand($"hostname \"{_activeMatch.Hostname}\"");
        Server.ExecuteCommand($"mp_friendlyfire {(_activeMatch.EnableFriendlyFire ? 1 : 0)}");

        AddTimer(3.0f, () =>
        {
            if (_activeMatch == null) return;

            Server.PrintToChatAll($"{ChatPrefix} Restaurando times da partida...");

            foreach (var playerSteamIdStr in _activeMatch.Team1.Players.Keys)
            {
                var player = Utilities.GetPlayerFromSteamId(ulong.Parse(playerSteamIdStr));
                if (player != null && player.IsValid && player.TeamNum != (byte)CsTeam.Terrorist)
                {
                    player.SwitchTeam(CsTeam.Terrorist);
                }
            }

            foreach (var playerSteamIdStr in _activeMatch.Team2.Players.Keys)
            {
                var player = Utilities.GetPlayerFromSteamId(ulong.Parse(playerSteamIdStr));
                if (player != null && player.IsValid && player.TeamNum != (byte)CsTeam.CounterTerrorist)
                {
                    player.SwitchTeam(CsTeam.CounterTerrorist);
                }
            }
        });
    }

    private void ApplyGotvSettings()
    {
        if (_gotvSettingsApplied) return;

        Server.PrintToConsole("[MatchKS] Aplicando configurações recomendadas para a SourceTV (GOTV)...");
        Server.ExecuteCommand("exec MatchKS/gotv.cfg");
        Server.PrintToConsole("[MatchKS] Configurações da GOTV aplicadas com sucesso.");
        _gotvSettingsApplied = true;
    }

    public HookResult OnGameEnd(EventGameEnd @event, GameEventInfo info)
    {
        if (_isMatchLive && _activeMatch != null)
        {
            WritePerMapPlayerStatsFile();
        }

        if (_isMatchLive && _activeMatch != null && _activeMatch.MapList.Count > 1)
        {
            StopDemoRecording();
            GoToNextMap();
        }
        else if (_isMatchLive)
        {
            EndFullMatch("A partida terminou.");
        }
        else
        {
            Server.PrintToChatAll($"{ChatPrefix} A partida terminou. O servidor irá carregar o próximo mapa em breve.");
            ResetMatchState();
        }
        return HookResult.Continue;
    }

    private void GoToNextMap()
    {
        if (_activeMatch == null) return;

        _activeMatch.CurrentMapIndex++;

        if (_activeMatch.CurrentMapIndex < _activeMatch.MapList.Count)
        {
            var nextMap = _activeMatch.MapList[_activeMatch.CurrentMapIndex];
            Server.PrintToChatAll($"{ChatPrefix} Fim do mapa! Carregando o próximo mapa ({nextMap.Name}) em 15 segundos.");
            
            SaveActiveMatch();

            AddTimer(15.0f, () => {
                Server.ExecuteCommand($"changelevel \"{nextMap.Name}\"");
            });
        }
        else
        {
            EndFullMatch("Todos os mapas da série foram concluídos.");
        }
    }

    private void SaveActiveMatch()
    {
        if (_activeMatch == null) return;
        try
        {
            File.WriteAllText(ActiveMatchFilePath, JsonSerializer.Serialize(_activeMatch, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Logger.LogError($"[MatchKS ERROR] Falha ao salvar partida ativa: {e.Message}");
        }
    }

    private void EndFullMatch(string? reason)
    {
        WriteMatchSummaryFile(reason);

        Server.PrintToChatAll(" ");
        Server.PrintToChatAll($"{ChatPrefix} ===== FIM DE JOGO =====");
        if (!string.IsNullOrEmpty(reason))
        {
            Server.PrintToChatAll($"{ChatPrefix} {reason}");
        }
        
        StopDemoRecording();

        
        Server.PrintToChatAll($"{ChatPrefix} A partida foi finalizada. O servidor será resetado para o estado padrão.");
        Server.PrintToChatAll(" ");

        _activeMatch = null;
        _isMatchLive = false;
        
        if (File.Exists(ActiveMatchFilePath))
        {
            try
            {
                File.Delete(ActiveMatchFilePath);
            }
            catch (Exception e)
            {
                Logger.LogError($"[MatchKS] Não foi possível deletar o arquivo active_match.json: {e.Message}");
            }
        }
        
        AddTimer(10.0f, () => Server.ExecuteCommand("exec gamemode_casual"));
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
                $"SeriesWinsTeam1={_activeMatch.Team1.MapsWon}",
                $"SeriesWinsTeam2={_activeMatch.Team2.MapsWon}"
            };

            if (!string.IsNullOrEmpty(_currentDemoName))
            {
                lines.Add($"Demo={_pluginConfig.DemoFolderPath}{_currentDemoName}.dem");
            }

            File.WriteAllLines(fullPath, lines);
            Logger.LogInformation($"[MatchKS] Resumo final da partida salvo em: {fullPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MatchKS ERROR] Falha ao salvar resumo final da partida: {ex.Message}");
        }
    }

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
        return TryReadIntByPath(player,
            "ActionTrackingServices.MatchStats.Kills",
            "ActionTrackingServices.MatchStats.EnemyKills",
            "InGameMoneyServices.Account.Kills",
            "Kills");
    }

    private int GetPlayerDeaths(CCSPlayerController player)
    {
        return TryReadIntByPath(player,
            "ActionTrackingServices.MatchStats.Deaths",
            "InGameMoneyServices.Account.Deaths",
            "Deaths");
    }

    private int TryReadIntByPath(object source, params string[] propertyPaths)
    {
        foreach (var path in propertyPaths)
        {
            var value = ReadNestedProperty(source, path);
            if (value == null) continue;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
            }
        }

        return 0;
    }

    private object? ReadNestedProperty(object source, string path)
    {
        object? current = source;

        foreach (var segment in path.Split('.'))
        {
            if (current == null) return null;

            var type = current.GetType();
            var property = type.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property == null) return null;

            current = property.GetValue(current);
        }

        return current;
    }

    private async Task SendMapStatsToDiscordAsync(List<PlayerStatsRow> rows, int roundsPlayed)
    {
        if (!_pluginConfig.DiscordWebhookEnabled) return;
        if (string.IsNullOrWhiteSpace(_pluginConfig.DiscordWebhookUrl)) return;

        try
        {
            var lines = new List<string>
            {
                $"Mapa: {Server.MapName}",
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

