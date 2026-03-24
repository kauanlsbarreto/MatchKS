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
    public override string ModuleVersion => "1.0.6";
    public override string ModuleAuthor => "Kauan";
    public static string ChatPrefixText = "MatchKS";
    public static string ChatPrefixColor = ChatColors.Blue.ToString();
    public static string ChatPrefix => $" {ChatPrefixColor}[{ChatPrefixText}]{ChatColors.Default}";

    public static MatchConfig? _activeMatch;
    public static PluginConfig _pluginConfig = new();
    private string MatchesConfigPath => Server.GameDirectory;
    private string ActiveMatchFilePath => Path.Combine(MatchesConfigPath, "active_match.json");
    private string PluginConfigPath => Path.Combine(FeseeCfgFolderPath, "config.cfg");
    private string MatchSummaryFolderPath => Path.Combine(FeseeCfgFolderPath, "history");
    private string CsgoRootPath
    {
        get
        {
            var normalized = Server.GameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var tail = Path.GetFileName(normalized);
            if (string.Equals(tail, "csgo", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return Path.Combine(normalized, "csgo");
        }
    }

    private string FeseeCfgFolderPath => Path.Combine(CsgoRootPath, "cfg", "MatchKS");

    private bool _isTeamTReady, _isTeamCTReady, _isMatchLive, _isKnifeRoundEnabledForCurrentMap, _isKnifeRoundActive;
    // Removido: _isTeamChangeLocked não é mais usado
    private CsTeam? _knifeRoundWinnerTeam;
    private int _team1TacPausesUsed, _team2TacPausesUsed;
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
    private CsTeam _techPauseTeam = CsTeam.None;
    private CsTeam _techPauseScheduledTeam = CsTeam.None;


    private bool _mapLogicHasRun = false;
    private Timer? _competitiveCheckTimer, _readyStatusTimer, _commandsAnnounceTimer;

    private bool _isSidePickPhase = false;
    private bool _isSideSwapPending = false;
    private Timer? _sidePickTimer;
    private Timer? _sidePickDisplayTimer;
    private int _sidePickCountdown = 0;
    private Timer? _autoSideSwapWindowTimer;
    private bool _mapArtifactsFinalized = false;
    private bool _isHandlingMatchEnd = false;

    private bool _crashRecoveryChecked = false;
    private bool _isWaitingForRestoreReady = false;
    private bool _isTeamTRestoreReady = false;
    private bool _isTeamCTRestoreReady = false;
    private Timer? _restoreReadyDisplayTimer;

    public override void Load(bool hotReload)
    {
        Logger.LogInformation($"[MatchKS DEBUG] Carregando Plugin v{ModuleVersion}...");

        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            _mapLogicHasRun = false;
            AddTimer(1.0f, RunMapStartLogic);
        });

        RegisterEventHandler<EventMapShutdown>(OnMapShutdownHandler);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeamChange);
        RegisterEventHandler<EventGameEnd>(OnGameEnd);
        RegisterEventHandler<EventCsWinPanelMatch>(OnMatchWinPanel);

        Logger.LogInformation("[MatchKS DEBUG] Eventos do jogo registrados com sucesso.");

        if (!Directory.Exists(FeseeCfgFolderPath)) { Directory.CreateDirectory(FeseeCfgFolderPath); }
        LoadConfigFromCfg();
        EnsureMatchKSCfgsExist();


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
                $"EnableOvertime={(_pluginConfig.EnableOvertime ? "true" : "false")}",
                $"OvertimeStartMoney={_pluginConfig.OvertimeStartMoney}",
                $"ChatPrefixText=\"{ChatPrefixText}\"",
                "ChatPrefixColor=\"blue\"",
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
        if (configDict.TryGetValue("EnableOvertime", out var enableOt) && bool.TryParse(enableOt, out var enableOtValue))
        {
            _pluginConfig.EnableOvertime = enableOtValue;
        }
        if (configDict.TryGetValue("OvertimeStartMoney", out var otMoney) && int.TryParse(otMoney, out var otMoneyValue) && otMoneyValue >= 0)
        {
            _pluginConfig.OvertimeStartMoney = otMoneyValue;
        }
        if (configDict.TryGetValue("ChatPrefixText", out var prefixText) && !string.IsNullOrWhiteSpace(prefixText))
        {
            ChatPrefixText = prefixText.Trim();
        }
        if (configDict.TryGetValue("ChatPrefixColor", out var prefixColor) && !string.IsNullOrWhiteSpace(prefixColor))
        {
            ChatPrefixColor = ResolveChatColor(prefixColor);
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


    private string GetDefaultTeamName(CsTeam team)
    {
        return team == CsTeam.Terrorist ? "Terroristas" : "Contra-Terroristas";
    }

    private void ResetMatchState()
    {

        _isMatchLive = false;
        _isKnifeRoundActive = false;
        _isSidePickPhase = false;

        _isTeamTReady = false;
        _isTeamCTReady = false;
        _knifeRoundWinnerTeam = null;
        _team1TacPausesUsed = 0;
        _team2TacPausesUsed = 0;

        _isPauseActive = false;
        _isPauseScheduled = false;
        _isTechPauseActive = false;
        _isTechPauseScheduled = false;
        _pausingTeam = null;
        _pausingTeamScheduled = CsTeam.None;
        _techPauseTeam = CsTeam.None;
        _techPauseScheduledTeam = CsTeam.None;
        _pauseCountdown = 0;

        _sidePickTimer?.Kill();
        _sidePickDisplayTimer?.Kill();
        _tacTimer?.Kill();
        _pauseDisplayTimer?.Kill();
        _isWaitingForRestoreReady = false;
        _isTeamTRestoreReady = false;
        _isTeamCTRestoreReady = false;
        _restoreReadyDisplayTimer?.Kill();

        _autoSideSwapWindowTimer?.Kill();
        _mapArtifactsFinalized = false;
        _isHandlingMatchEnd = false;
        _playerDamageInfo.Clear();
        _mapDamageByPlayer.Clear();

        if (_votoMapaPendente)
            CancelarVotoMapa("partida reiniciada");
        _votoMapaTimer?.Kill();
        _votoMapaDisplayTimer?.Kill();
        _votoMapaPendente = false;
        _votoMapaAceitaram.Clear();

        Logger.LogInformation("[MatchKS] O estado da partida foi reiniciado.");
    }

    private void ReinitializeWarmupState()
    {
        _mapLogicHasRun = false;

        AddTimer(1.0f, () => Server.ExecuteCommand("exec MatchKS/warmup.cfg"));
        AddTimer(2.0f, RunMapStartLogic);
        AddTimer(3.0f, AnnounceReadyStatus);
        AddTimer(15.2f, AnnounceMatchCommands);
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

        if (_isMatchLive && !_isKnifeRoundActive)
        {
            var teamEntities = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            var teamCt = teamEntities.FirstOrDefault(t => t.TeamNum == (byte)CsTeam.CounterTerrorist);
            var teamTr = teamEntities.FirstOrDefault(t => t.TeamNum == (byte)CsTeam.Terrorist);
            int totalRounds = (teamCt?.Score ?? 0) + (teamTr?.Score ?? 0);

            if (totalRounds == 12)
            {
                ApplyTrackedSideSwap();
                _autoSideSwapWindowTimer?.Kill();
                _autoSideSwapWindowTimer = AddTimer(12.0f, () => { });
            }
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnMatchWinPanel(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        
        if (!_isMatchLive) return HookResult.Continue;

        Logger.LogInformation("[MatchKS] WinPanel detectado. Iniciando procedimentos de fim de partida...");
        HandleMatchEnd("Partida finalizada (Placar Final).");
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

    private void CreateDefaultPugMatch()
    {
        _activeMatch = new MatchConfig
        {
            Team1 = new MatchTeam { Name = "Terroristas", Tag = "TR" },
            Team2 = new MatchTeam { Name = "Contra-Terroristas", Tag = "CT" },
            EnableOvertime = _pluginConfig.EnableOvertime
        };
    }

    public HookResult OnMapShutdownHandler(EventMapShutdown @event, GameEventInfo info)
    {
        FinalizeCurrentMapArtifacts();
        _mapLogicHasRun = false;
        return HookResult.Continue;
    }

    private void RunMapStartLogic()
    {
        if (_mapLogicHasRun) return;
        _mapLogicHasRun = true;

        Logger.LogInformation($"[MatchKS] Lógica de início de mapa executada para '{Server.MapName}'.");

        if (_activeMatch == null)
        {
            Logger.LogInformation("[MatchKS] Criando PUG padrão.");
            CreateDefaultPugMatch();
        }

        _mapDamageByPlayer.Clear();
        _mapArtifactsFinalized = false;
        _isHandlingMatchEnd = false;
        ResetMapStates();
        _isKnifeRoundEnabledForCurrentMap = _pluginConfig.RoundFaca;

        Server.ExecuteCommand("exec MatchKS/warmup.cfg");
        Server.PrintToConsole("[MatchKS] Executed warmup.cfg.");

        _readyStatusTimer?.Kill();
        _competitiveCheckTimer?.Kill();
        _commandsAnnounceTimer?.Kill();

        _readyStatusTimer = AddTimer(15.0f, AnnounceReadyStatus, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        _competitiveCheckTimer = AddTimer(15.0f, CheckCompetitiveMode, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        _commandsAnnounceTimer = AddTimer(15.0f, () =>
        {
            if (_isMatchLive) { _commandsAnnounceTimer?.Kill(); return; }
            AnnounceMatchCommands();
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

        _crashRecoveryChecked = false;
        AddTimer(20.0f, CheckForCrashRecovery);
    }

    private void EnsureMatchKSCfgsExist()
    {
        var cfgDir = Path.Join(Server.GameDirectory, "csgo", "cfg", "MatchKS");
        try
        {
            Directory.CreateDirectory(cfgDir);

            var warmupPath = Path.Combine(cfgDir, "warmup.cfg");
            if (!File.Exists(warmupPath))
            {
                File.WriteAllLines(warmupPath, new[]
                {
                    "// MatchKS - Configuração de Aquecimento",
                    "// Este arquivo é criado automaticamente. Edite conforme necessário.",
                    "mp_warmuptime 999",
                    "mp_buy_anywhere 1",
                    "mp_buytime 9999",
                    "mp_autoteambalance 0",
                    "mp_limitteams 0",
                    "mp_endwarmup_player_count 0",
                    "mp_friendlyfire 0",
                    "sv_cheats 0",
                    "mp_freezetime 6",
                });
                Logger.LogInformation($"[MatchKS] warmup.cfg criado em: {warmupPath}");
            }

            var knifePath = Path.Combine(cfgDir, "knife.cfg");
            if (!File.Exists(knifePath))
            {
                File.WriteAllLines(knifePath, new[]
                {
                    "// MatchKS - Configuração do Round de Faca",
                    "// Este arquivo é criado automaticamente. Edite conforme necessário.",
                    "mp_maxrounds 2",
                    "mp_roundtime 2",
                    "mp_roundtime_defuse 2",
                    "mp_startmoney 0",
                    "mp_maxmoney 0",
                    "mp_buy_anywhere 0",
                    "mp_friendlyfire 0",
                    "sv_cheats 0",
                    "mp_freezetime 6",
                });
                Logger.LogInformation($"[MatchKS] knife.cfg criado em: {knifePath}");
            }

            var livePath = Path.Combine(cfgDir, "live.cfg");
            if (!File.Exists(livePath))
            {
                var ffDefault = _pluginConfig.FogoAmigo ? 1 : 0;
                var otDefault = _pluginConfig.EnableOvertime ? 1 : 0;
                var otMoneyDefault = _pluginConfig.OvertimeStartMoney;
                File.WriteAllLines(livePath, new[]
                {
                    "// MatchKS - Configuração de Partida Ao Vivo",
                    "// mp_friendlyfire, mp_overtime_enable e mp_overtime_startmoney são sincronizados pelo plugin.",
                    $"mp_friendlyfire {ffDefault}",
                    $"mp_overtime_enable {otDefault}",
                    "mp_overtime_maxrounds 6",
                    $"mp_overtime_startmoney {otMoneyDefault}",
                    "mp_maxrounds 24",
                    "sv_cheats 0",
                    "mp_autoteambalance 0",
                    "mp_limitteams 0",
                    "mp_buytime 20",
                    "mp_buy_anywhere 0",
                    "mp_freezetime 15",
                    "mp_roundtime 1.92",
                    "mp_roundtime_defuse 1.92",
                });
                Logger.LogInformation($"[MatchKS] live.cfg criado em: {livePath}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MatchKS] Erro ao verificar/criar CFGs padrão: {ex.Message}");
        }
    }

    private void WriteSessionInfo(int round)
    {
        if (_activeMatch == null) return;
        var dir = GetCurrentBackupDirectory();
        if (!Directory.Exists(dir)) return;
        try
        {
            var info = new BackupSessionInfo
            {
                Team1Players = new Dictionary<string, string>(_activeMatch.Team1.Players),
                Team2Players = new Dictionary<string, string>(_activeMatch.Team2.Players),
                Team1Name = _activeMatch.Team1.Name,
                Team2Name = _activeMatch.Team2.Name,
                MapName = Server.MapName,
                LastBackupRound = round,
                MatchEnded = false
            };
            File.WriteAllText(
                Path.Combine(dir, "session_info.json"),
                JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MatchKS] Erro ao salvar session_info.json: {ex.Message}");
        }
    }

    private void MarkSessionEnded()
    {
        var dir = GetCurrentBackupDirectory();
        var path = Path.Combine(dir, "session_info.json");
        if (!File.Exists(path)) return;
        try
        {
            var info = JsonSerializer.Deserialize<BackupSessionInfo>(File.ReadAllText(path));
            if (info == null) return;
            info.MatchEnded = true;
            File.WriteAllText(path, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MatchKS] Erro ao marcar sessão encerrada: {ex.Message}");
        }
    }

    private void CheckForCrashRecovery()
    {
        if (_crashRecoveryChecked || _isMatchLive) return;
        _crashRecoveryChecked = true;

        var root = GetBackupRootPath();
        if (!Directory.Exists(root)) return;

        var currentIds = Utilities.GetPlayers()
            .Where(p => p.IsValid && !p.IsBot && p.Connected == PlayerConnectedState.PlayerConnected)
            .Select(p => p.SteamID.ToString())
            .ToHashSet();

        if (currentIds.Count == 0) return;

        try
        {
            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly)
                                         .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d)))
            {
                var sessionPath = Path.Combine(dir, "session_info.json");
                if (!File.Exists(sessionPath)) continue;

                BackupSessionInfo? info;
                try { info = JsonSerializer.Deserialize<BackupSessionInfo>(File.ReadAllText(sessionPath)); }
                catch { continue; }

                if (info == null || info.MatchEnded || info.LastBackupRound <= 0) continue;

                var sessionIds = info.Team1Players.Keys.Concat(info.Team2Players.Keys).ToHashSet();
                if (sessionIds.Count == 0) continue;

                int overlap = sessionIds.Count(id => currentIds.Contains(id));
                int threshold = Math.Max(1, (int)Math.Ceiling(sessionIds.Count * 0.7));
                if (overlap < threshold) continue;

                var backups = GetBackupsFromDirectory(dir);
                int lastRound = backups.Count > 0 ? backups.Max(b => b.Round) : info.LastBackupRound;

                Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}[CRASH RECOVERY]{ChatColors.Default} Partida anterior detectada: {ChatColors.Gold}{info.Team1Name}{ChatColors.Default} vs {ChatColors.Gold}{info.Team2Name}{ChatColors.Default} | Mapa: {ChatColors.Green}{info.MapName}{ChatColors.Default}");
                Server.PrintToChatAll($"{ChatPrefix} Round {ChatColors.Green}{lastRound}{ChatColors.Default} disponível ({overlap}/{sessionIds.Count} jogadores reconectados).");
                Server.PrintToChatAll($"{ChatPrefix} Dê {ChatColors.Green}.ready{ChatColors.Default} nos dois times. Após partir ficará {ChatColors.Green}ao vivo{ChatColors.Default}, o {ChatColors.Red}admin{ChatColors.Default} usa {ChatColors.Green}.restore {lastRound}{ChatColors.Default} para restaurar.");
                break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MatchKS] Erro no crash recovery check: {ex.Message}");
        }
    }

    internal void SyncLiveCfgFromPluginConfig()
    {
        var livePath = Path.Join(Server.GameDirectory, "csgo", "cfg", "MatchKS", "live.cfg");
        var ffValue = _pluginConfig.FogoAmigo ? 1 : 0;
        var otValue = (_activeMatch?.EnableOvertime ?? _pluginConfig.EnableOvertime) ? 1 : 0;
        var otMoneyValue = _pluginConfig.OvertimeStartMoney;

        try
        {
            if (!File.Exists(livePath))
            {
                EnsureMatchKSCfgsExist();
                if (!File.Exists(livePath)) return;
            }

            var lines = File.ReadAllLines(livePath).ToList();
            bool ffFound = false, otFound = false, otMoneyFound = false;

            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("mp_friendlyfire", StringComparison.OrdinalIgnoreCase)
                    && !trimmed.StartsWith("//"))
                {
                    lines[i] = $"mp_friendlyfire {ffValue}";
                    ffFound = true;
                }
                else if (trimmed.StartsWith("mp_overtime_enable", StringComparison.OrdinalIgnoreCase)
                         && !trimmed.StartsWith("//"))
                {
                    lines[i] = $"mp_overtime_enable {otValue}";
                    otFound = true;
                }
                else if (trimmed.StartsWith("mp_overtime_startmoney", StringComparison.OrdinalIgnoreCase)
                         && !trimmed.StartsWith("//"))
                {
                    lines[i] = $"mp_overtime_startmoney {otMoneyValue}";
                    otMoneyFound = true;
                }
            }

            if (!ffFound) lines.Add($"mp_friendlyfire {ffValue}");
            if (!otFound) lines.Add($"mp_overtime_enable {otValue}");
            if (!otMoneyFound) lines.Add($"mp_overtime_startmoney {otMoneyValue}");

            File.WriteAllLines(livePath, lines);
            Logger.LogInformation($"[MatchKS] live.cfg sincronizado — FogoAmigo={ffValue}, Overtime={otValue}, OvertimeMoney={otMoneyValue}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MatchKS] Erro ao sincronizar live.cfg: {ex.Message}");
        }
    }

    public HookResult OnGameEnd(EventGameEnd @event, GameEventInfo info)
    {
        HandleMatchEnd("A partida terminou.");
        return HookResult.Continue;
    }

    private void HandleMatchEnd(string reason)
    {
        if (_isHandlingMatchEnd) return;

        if (!_isMatchLive)
        {
            Server.PrintToChatAll($"{ChatPrefix} Mapa encerrado. Voltando ao estado de aquecimento.");
            ResetMatchState();
            ReinitializeWarmupState();
            return;
        }

        _isHandlingMatchEnd = true;
        FinalizeCurrentMapArtifacts();

        EndFullMatch(reason);
    }

    private void FinalizeCurrentMapArtifacts()
    {
        if (_mapArtifactsFinalized) return;
        if (!_isMatchLive || _activeMatch == null) return;

        _mapArtifactsFinalized = true;
        WritePerMapPlayerStatsFile();
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
        
        MarkSessionEnded();

        Server.PrintToChatAll($"{ChatPrefix} A partida foi finalizada. O servidor será resetado para o estado padrão.");
        Server.PrintToChatAll(" ");

        ResetMatchState();
        _activeMatch = null;
        
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

        ReinitializeWarmupState();
    }

}
