using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Cvars;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace MatchKS;

public partial class MatchKS
{

    private sealed class RoundBackupEntry
    {
        public int Round { get; init; }
        public string AbsolutePath { get; init; } = string.Empty;
        public DateTime LastWriteTimeUtc { get; init; }
    }

    private bool IsInFreezetime()
    {
        var gameRules = GetGameRules();
        return gameRules is { FreezePeriod: true };
    }
    private CCSGameRules? GetGameRules()
    {
        var gameRulesEntities = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules");
        return gameRulesEntities.FirstOrDefault()?.GameRules;
    }

    private string GetBackupRootPath()
    {
        return Path.Combine(CsgoRootPath, "BackupMatchKS");
    }

    private string GetCurrentBackupStorageKey()
    {
        var mapName = SanitizeFileName(Server.MapName);
        if (_activeMatch == null)
        {
            return $"pug_{mapName}";
        }

        var mapNumber = 1;
        var team1 = SanitizeFileName(string.IsNullOrWhiteSpace(_activeMatch.Team1.Name) ? "Terroristas" : _activeMatch.Team1.Name);
        var team2 = SanitizeFileName(string.IsNullOrWhiteSpace(_activeMatch.Team2.Name) ? "Contra-Terroristas" : _activeMatch.Team2.Name);
        return $"m{mapNumber}_{mapName}_{team1}_vs_{team2}";
    }

    private string GetCurrentBackupDirectory()
    {
        return Path.Combine(GetBackupRootPath(), GetCurrentBackupStorageKey());
    }

    private static bool TryParseRoundFromBackupFileName(string fileName, out int round)
    {
        round = 0;
        const string prefix = "round_";

        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = fileName[prefix.Length..];
        var separatorIndex = suffix.IndexOf('_');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var roundToken = suffix[..separatorIndex];
        if (!int.TryParse(roundToken, out round))
        {
            return false;
        }

        return round > 0;
    }

    private static string ToCsgoRelativePath(string csgoRootPath, string absolutePath)
    {
        return Path.GetRelativePath(csgoRootPath, absolutePath).Replace('\\', '/');
    }

    private List<RoundBackupEntry> GetBackupsForCurrentGame()
    {
        var backupDirectory = GetCurrentBackupDirectory();
        return GetBackupsFromDirectory(backupDirectory);
    }

    private List<RoundBackupEntry> GetBackupsFromDirectory(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return new List<RoundBackupEntry>();
        }

        var allFiles = Directory
            .GetFiles(backupDirectory, "round_*", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var ext = Path.GetExtension(path);
                return ext.Equals(".cfg", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(ext);
            });

        var backups = new List<RoundBackupEntry>();
        foreach (var filePath in allFiles)
        {
            var fileName = Path.GetFileName(filePath);
            if (!TryParseRoundFromBackupFileName(fileName, out var round))
            {
                continue;
            }

            backups.Add(new RoundBackupEntry
            {
                Round = round,
                AbsolutePath = filePath,
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath)
            });
        }

        return backups
            .GroupBy(b => b.Round)
            .Select(group => group.OrderByDescending(item => item.LastWriteTimeUtc).First())
            .OrderBy(item => item.Round)
            .ToList();
    }

    private string? GetLatestBackupDirectory()
    {
        var root = GetBackupRootPath();
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .GetDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => Directory.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
    }

    private List<RoundBackupEntry> GetBackupsForCurrentGameOrLatest(out string sourceLabel)
    {
        var currentDirectory = GetCurrentBackupDirectory();
        var currentBackups = GetBackupsFromDirectory(currentDirectory);
        if (currentBackups.Count > 0)
        {
            sourceLabel = Path.GetFileName(currentDirectory);
            return currentBackups;
        }

        var latestDirectory = GetLatestBackupDirectory();
        if (string.IsNullOrWhiteSpace(latestDirectory))
        {
            sourceLabel = "";
            return new List<RoundBackupEntry>();
        }

        var latestBackups = GetBackupsFromDirectory(latestDirectory);
        sourceLabel = Path.GetFileName(latestDirectory);
        return latestBackups;
    }

    private int GetCurrentRoundOneBased()
    {
        var teamEntities = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
        var teamCt = teamEntities.FirstOrDefault(t => t.TeamNum == (byte)CsTeam.CounterTerrorist);
        var teamTr = teamEntities.FirstOrDefault(t => t.TeamNum == (byte)CsTeam.Terrorist);

        int roundsPlayed = (teamCt?.Score ?? 0) + (teamTr?.Score ?? 0);
        return Math.Max(1, roundsPlayed + 1);
    }

    private void CreateRoundBackupForLiveMatch()
    {
        var round = GetCurrentRoundOneBased();
        if (round <= 0)
        {
            return;
        }

        var backupDirectory = GetCurrentBackupDirectory();
        if (!Directory.Exists(backupDirectory))
        {
            Directory.CreateDirectory(backupDirectory);
        }

        var fileName = $"round_{round:00}_{DateTime.Now:yyyyMMdd_HHmmss}.cfg";
        var relativeBackupPath = Path.Combine("BackupMatchKS", GetCurrentBackupStorageKey(), fileName).Replace('\\', '/');

        Server.ExecuteCommand($"mp_backup_round_file \"{relativeBackupPath}\"");
        WriteSessionInfo(round);
    }

    private void PrintBackupsToPlayer(CCSPlayerController? player, List<RoundBackupEntry> backups)
    {
        if (player == null)
        {
            Server.PrintToConsole($"[MatchKS] Backups encontrados para este jogo: {backups.Count}");
            foreach (var item in backups)
            {
                var relative = ToCsgoRelativePath(CsgoRootPath, item.AbsolutePath);
                Server.PrintToConsole($"[MatchKS] Round {item.Round}: {relative}");
            }
            return;
        }

        player.PrintToChat($"{ChatPrefix} Backups encontrados para este jogo: {ChatColors.Green}{backups.Count}");
        foreach (var item in backups)
        {
            var relative = ToCsgoRelativePath(CsgoRootPath, item.AbsolutePath);
            player.PrintToChat($"{ChatPrefix} Round {ChatColors.Green}{item.Round}{ChatColors.Default}: {relative}");
        }
    }

    [ConsoleCommand("css_map"), RequiresPermissions("@css/kick")]
    public void OnMapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            player?.PrintToChat("Uso: .map <nome_do_mapa>");
            return;
        }

        var mapName = command.GetArg(1);

        AddTimer(0.5f, () =>
        {
            Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} está trocando o mapa para {ChatColors.Green}{mapName}{ChatColors.Default}.");
            Server.ExecuteCommand($"changelevel \"{mapName}\"");
        });
    }

    [ConsoleCommand("css_kill"), RequiresPermissions("@css/kick")]
    public void OnKillCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        var adminTeam = player.TeamNum;
        if (adminTeam != (byte)CsTeam.Terrorist && adminTeam != (byte)CsTeam.CounterTerrorist)
        {
            player.PrintToChat($"{ChatPrefix} Você precisa estar em um time (CT ou TR) para usar este comando.");
            return;
        }

        var enemyTeam = adminTeam == (byte)CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        string enemyTeamName = enemyTeam == CsTeam.CounterTerrorist ? "Contra-Terroristas" : "Terroristas";

        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} eliminou o time {enemyTeamName}!");

        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && p.TeamNum == (byte)enemyTeam))
        {
            p.PlayerPawn.Value?.CommitSuicide(false, true);
        }
    }

    [ConsoleCommand("css_slap"), RequiresPermissions("@css/kick")]
    public void OnSlapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;

        var players = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot).ToList();

        if (command.ArgCount < 2)
        {
            player.PrintToChat($"{ChatPrefix} Jogadores disponíveis para slap:");
            for (int i = 0; i < players.Count; i++)
            {
                player.PrintToChat($" {ChatColors.Green}{i + 1}{ChatColors.Default}: {players[i].PlayerName}");
            }
            player.PrintToChat($"{ChatPrefix} Uso: {ChatColors.Green}.slap <número> [tapas]{ChatColors.Default}");
            return;
        }

        if (!int.TryParse(command.GetArg(1), out int playerNumber) || playerNumber < 1 || playerNumber > players.Count)
        {
            player.PrintToChat($"{ChatPrefix} Número de jogador inválido. Use {ChatColors.Green}.slap{ChatColors.Default} para ver a lista.");
            return;
        }

        int slapCount = command.ArgCount > 2 && int.TryParse(command.GetArg(2), out int parsedSlaps) ? parsedSlaps : 1;
        if (slapCount < 1) slapCount = 1;

        var targetPlayer = players[playerNumber - 1];

        if (targetPlayer.PawnIsAlive)
        {
            Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} deu {slapCount} tapa(s) em {ChatColors.Lime}{targetPlayer.PlayerName}{ChatColors.Default}!");

            for (int i = 0; i < slapCount; i++)
            {
                AddTimer(i * 0.2f, () => {
                    if (targetPlayer.PawnIsAlive && targetPlayer.PlayerPawn.Value != null)
                        targetPlayer.PlayerPawn.Value.AbsVelocity.Z += 350.0f;
                });
            }
        }
    }




    [ConsoleCommand("css_config")]
    public void OnMatchConfigCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_activeMatch == null)
        {
            player?.PrintToChat($"{MatchKS.ChatPrefix} Nenhuma partida está ativa/configurada.");
            return;
        }

        string roundsParaGanhar = "13 rounds (padrão)";

        string matchState = _isMatchLive
            ? $"{ChatColors.Green}AO VIVO"
            : $"{ChatColors.Yellow}AQUECIMENTO";

        string overtimeStatus = _activeMatch.EnableOvertime ? $"{ChatColors.Green}Liberado" : $"{ChatColors.Red}Bloqueado";
        string knifeStatus = _isKnifeRoundEnabledForCurrentMap ? $"{ChatColors.Green}SIM" : $"{ChatColors.Red}NAO";
        string ffStatus = _pluginConfig.FogoAmigo ? $"{ChatColors.Red}SIM" : $"{ChatColors.Green}NAO";

        string configDisplay = $"{ChatColors.Default}================== {ChatColors.Yellow}CONFIGURAÇÃO DO MATCH{ChatColors.Default} ==================\r\n" +
                       $"{ChatColors.Default}  • Estado da Partida: {matchState}{ChatColors.Default}\r\n" +
                       $"{ChatColors.Default}  • Mapa Atual: {ChatColors.Yellow}{Server.MapName}{ChatColors.Default}\r\n" +
                       $"{ChatColors.Default}  • Modo de Jogo: {ChatColors.Yellow}{_activeMatch.GameMode}{ChatColors.Default}\r\n" +
                       $"{ChatColors.Default}  • Rounds para Vencer: {ChatColors.Yellow}{roundsParaGanhar}{ChatColors.Default}\r\n" +
                       $"{ChatColors.Default}  • Overtime: {overtimeStatus}{ChatColors.Default}\r\n" +
                       $"{ChatColors.Default}  • Round de Faca: {knifeStatus}{ChatColors.Default}\r\n" +
                       $"{ChatColors.Default}  • Fogo Amigo (config): {ffStatus}{ChatColors.Default}\r\n" +
                       $"{ChatColors.Default}  • Pausas Táticas por Time: {ChatColors.Yellow}{_pluginConfig.PausesTaticoPorEquipe}{ChatColors.Default}\r\n" +
                       $"{ChatColors.Default}  • Duração da Pausa Tática: {ChatColors.Yellow}{_pluginConfig.DuracaoPauseTatico}s{ChatColors.Default}\r\n" +
                       $"{ChatColors.Default}=======================================================";

        player?.PrintToChat(configDisplay);
    }

    [ConsoleCommand("css_start"), ConsoleCommand("css_comecar"), RequiresPermissions("@css/kick")]
    public void OnForceStartCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_activeMatch == null || _isMatchLive)
        {
            player?.PrintToChat($"{MatchKS.ChatPrefix} A partida não pode ser forçada agora.");
            return;
        }

        Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} forçou o início da partida!");
        _isTeamTReady = true;
        _isTeamCTReady = true;
        CheckIfMatchCanStart();
    }

    private void HandleSidePickCommand(CCSPlayerController? player, bool swapSides)
    {
        if (player == null || !player.IsValid) return;

        if (!_isSidePickPhase)
        {
            player.PrintToChat($"{ChatPrefix} Não é o momento de escolher os lados.");
            return;
        }

        if (_knifeRoundWinnerTeam == null || player.TeamNum != (byte)_knifeRoundWinnerTeam)
        {
            player.PrintToChat($"{ChatPrefix} Apenas o time que venceu o round de faca pode escolher o lado.");
            return;
        }
        
        HandleSidePickDecision(swapSides);
    }

    [ConsoleCommand("css_restart"), ConsoleCommand("css_recomecar"), RequiresPermissions("@css/kick")]
    public void OnRestartMatchCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_activeMatch == null)
        {
            player?.PrintToChat($"{MatchKS.ChatPrefix} Nenhuma partida ativa para reiniciar.");
            return;
        }

        Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} reiniciou a partida de volta para o aquecimento.");

        ResetMapStates();

        Server.ExecuteCommand("exec MatchKS/warmup.cfg");

        AddTimer(1.0f, () =>
        {
            Server.ExecuteCommand("mp_restartgame 1");
            Server.PrintToChatAll($"{MatchKS.ChatPrefix} Partida reiniciada. Aguardando prontidão dos times.");
        });

        _readyStatusTimer?.Kill();
        _readyStatusTimer = AddTimer(15.0f, AnnounceReadyStatus, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    [ConsoleCommand("css_adm"), RequiresPermissions("@css/kick")]
    public void OnAdminChatCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2) { player?.PrintToChat("Uso: .adm <mensagem>"); return; }
        var message = command.ArgString.Substring(command.GetArg(0).Length).Trim();
        Server.PrintToChatAll($" {ChatColors.Red}[ADM - MatchKS]{ChatColors.Default} {message}");
    }

    [ConsoleCommand("css_backups")]
    public void OnBackupsCommand(CCSPlayerController? player, CommandInfo command)
    {
        var backups = GetBackupsForCurrentGameOrLatest(out var sourceLabel);
        if (backups.Count == 0)
        {
            player?.PrintToChat($"{ChatPrefix} Nenhum backup encontrado para este jogo.");
            if (player == null)
            {
                Server.PrintToConsole("[MatchKS] Nenhum backup encontrado para este jogo.");
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(sourceLabel))
        {
            player?.PrintToChat($"{ChatPrefix} Origem do backup: {ChatColors.Green}{sourceLabel}");
        }

        PrintBackupsToPlayer(player, backups);
    }

    [ConsoleCommand("css_restore"), RequiresPermissions("@css/kick")]
    public void OnRestoreRoundCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_isMatchLive)
        {
            player?.PrintToChat($"{ChatPrefix} O restore só pode ser usado com a partida {ChatColors.Green}ao vivo{ChatColors.Default}. Dê .ready nos dois times primeiro.");
            return;
        }

        if (command.ArgCount < 2)
        {
            player?.PrintToChat($"{ChatPrefix} Uso: .restore <round>");
            return;
        }

        if (!int.TryParse(command.GetArg(1), out int roundToRestore) || roundToRestore <= 0)
        {
            player?.PrintToChat($"{ChatPrefix} Round inválido. Use valores começando em 1 (sem round 0).");
            return;
        }

        var backups = GetBackupsForCurrentGameOrLatest(out var sourceLabel);
        var targetBackup = backups.FirstOrDefault(b => b.Round == roundToRestore);
        if (targetBackup == null)
        {
            var availableRounds = backups.Select(b => b.Round.ToString()).ToList();
            if (availableRounds.Count == 0)
            {
                player?.PrintToChat($"{ChatPrefix} Nenhum backup disponível para restore.");
            }
            else
            {
                player?.PrintToChat($"{ChatPrefix} Backup do round {roundToRestore} não encontrado. Rounds disponíveis: {string.Join(", ", availableRounds)}");
            }
            return;
        }

        var relativePath = ToCsgoRelativePath(CsgoRootPath, targetBackup.AbsolutePath);
        Server.ExecuteCommand($"mp_backup_restore_load_file \"{relativePath}\"");
        if (!string.IsNullOrWhiteSpace(sourceLabel))
        {
            Server.PrintToChatAll($"{ChatPrefix} Fonte selecionada: {ChatColors.Green}{sourceLabel}{ChatColors.Default}.");
        }

        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} restaurou o backup do round {ChatColors.Green}{roundToRestore}{ChatColors.Default}.");
        InitiateRestorePause();
    }



    [ConsoleCommand("css_rk"), RequiresPermissions("@css/root")]
    public void OnKnifeRoundToggle(CCSPlayerController? player, CommandInfo command)
    {
        _isKnifeRoundEnabledForCurrentMap = !_isKnifeRoundEnabledForCurrentMap;
        Server.PrintToChatAll($"{MatchKS.ChatPrefix} Admin Override: Round de Faca para este mapa está agora {(_isKnifeRoundEnabledForCurrentMap ? "HABILITADO" : "DESABILITADO")}.");
    }
    
    [ConsoleCommand("css_skinsperso"), RequiresPermissions("@css/kick")]
    public void OnToggleSkinCheckerCommand(CCSPlayerController? player, CommandInfo command)
    {
        _isSkinCheckerEnabled = !_isSkinCheckerEnabled;
        string status = _isSkinCheckerEnabled ? "ATIVADA" : "DESATIVADA";
        string statusColor = _isSkinCheckerEnabled ? ChatColors.Green.ToString() : ChatColors.Red.ToString();

        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} alterou a verificação de skins. Status: {statusColor}{status}");
    }

    [ConsoleCommand("css_nobots"), RequiresPermissions("@css/kick")]
    public void OnNoBotsCommand(CCSPlayerController? player, CommandInfo command)
    {
        Server.ExecuteCommand("bot_join_after_player 0");
        Server.ExecuteCommand("bot_kick");
        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} removeu os bots. Bots não entrarão automaticamente.");
    }

    [ConsoleCommand("css_bots"), RequiresPermissions("@css/kick")]
    public void OnBotsCommand(CCSPlayerController? player, CommandInfo command)
    {
        Server.ExecuteCommand("bot_quota_mode normal");
        Server.ExecuteCommand("bot_join_after_player 1");

        int humanPlayers = Utilities.GetPlayers().Count(p => p.IsValid && !p.IsBot && p.Connected == PlayerConnectedState.PlayerConnected);
        int botsNeeded = 10 - humanPlayers;
        if (botsNeeded < 0) botsNeeded = 0;

        Server.ExecuteCommand($"bot_quota {botsNeeded}");
        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} adicionou bots para completar os times.");
    }

    [ConsoleCommand("css_stay"), ConsoleCommand("css_ficar")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnStayCommand(CCSPlayerController? player, CommandInfo command)
    {
        HandleSidePickCommand(player, swapSides: false);
    }

    [ConsoleCommand("css_switch"), ConsoleCommand("css_trocar")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSwitchCommand(CCSPlayerController? player, CommandInfo command)
    {
        HandleSidePickCommand(player, swapSides: true);
    }

    
    [ConsoleCommand("css_tec"), ConsoleCommand("css_tech")]
    public void OnTechPauseCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_isMatchLive) return;

        var callerTeam = player != null && player.IsValid &&
                         (player.TeamNum == (byte)CsTeam.Terrorist || player.TeamNum == (byte)CsTeam.CounterTerrorist)
            ? (CsTeam)player.TeamNum
            : CsTeam.None;
        
        if (_isTechPauseActive)
        {
            if (callerTeam != CsTeam.None && _techPauseTeam != CsTeam.None && callerTeam != _techPauseTeam)
            {
                player?.PrintToChat($"{ChatPrefix} Apenas o time {GetTeamName((byte)_techPauseTeam)} pode despausar esta pausa técnica.");
                return;
            }

            UnpauseMatch(isTechPause: true);
            return;
        }

        if (_isTechPauseScheduled)
        {
            if (callerTeam != CsTeam.None && callerTeam == _techPauseScheduledTeam)
            {
                _isTechPauseScheduled = false;
                _techPauseScheduledTeam = CsTeam.None;
                Server.PrintToChatAll($"{ChatPrefix} O time {GetTeamName((byte)callerTeam)} cancelou a pausa técnica agendada.");
            }
            else
            {
                player?.PrintToChat($"{ChatPrefix} Já existe uma pausa técnica agendada.");
            }

            return;
        }

        if (_isPauseActive)
        {
            player?.PrintToChat($"{ChatPrefix} Uma pausa tática já está em andamento.");
            return;
        }

        if (IsInFreezetime())
        {
            StartTechPause(callerTeam);
        }
        else
        {
            _isTechPauseScheduled = true;
            _techPauseScheduledTeam = callerTeam;

            var requesterName = callerTeam == CsTeam.None
                ? $"{ChatColors.Red}ADMIN{ChatColors.Default}"
                : $"o time {GetTeamName((byte)callerTeam)}";

            Server.PrintToChatAll($"{ChatPrefix} {requesterName} agendou uma pausa técnica para o próximo round.");
        }
    }
    
    [ConsoleCommand("css_tac"), ConsoleCommand("css_timeout")]
    public void OnTacCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !_isMatchLive || _isPauseActive || _isPauseScheduled || _isTechPauseActive || _isTechPauseScheduled)
        {
            player?.PrintToChat($"{ChatPrefix} Não é possível pedir pausa agora.");
            return;
        }

        int pausesUsed = player.TeamNum == (byte)CsTeam.Terrorist ? _team1TacPausesUsed : _team2TacPausesUsed;
        if (pausesUsed >= _pluginConfig.PausesTaticoPorEquipe)
        {
            player.PrintToChat($"{ChatPrefix} Seu time não tem mais pausas táticas.");
            return;
        }

        _pausingTeamScheduled = (CsTeam)player.TeamNum;

        if (IsInFreezetime())
        {
            StartTacticalPause(); 
        }
        else
        {
            _isPauseScheduled = true;
            var teamName = GetTeamName(player.TeamNum);
            Server.PrintToChatAll($"{ChatPrefix} O time {teamName} agendou uma pausa tática para o próximo round.");
        }
    }
    
    private void StartTechPause(CsTeam requestingTeam = CsTeam.None)
    {
        _techPauseTeam = requestingTeam != CsTeam.None ? requestingTeam : _techPauseScheduledTeam;
        _techPauseScheduledTeam = CsTeam.None;
        _isTechPauseActive = true;
        Server.ExecuteCommand("mp_pause_match");

        var requesterName = _techPauseTeam == CsTeam.None
            ? $"{ChatColors.Red}ADMIN{ChatColors.Default}"
            : $"o time {GetTeamName((byte)_techPauseTeam)}";

        var centerMessage = _techPauseTeam == CsTeam.None
            ? "Admin, digite .tec para continuar"
            : $"{GetTeamName((byte)_techPauseTeam)}, digite .tec para continuar";

        Server.PrintToChatAll($"{ChatPrefix} {requesterName} pausou a partida (pausa técnica). Use {ChatColors.Green}.tec{ChatColors.Default} para despausar.");
        
        _pauseDisplayTimer?.Kill();
        _pauseDisplayTimer = AddTimer(1.0f, () =>
        {
            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
            {
                p.PrintToCenterHtml($"<font color='orange'>PARTIDA EM PAUSA TÉCNICA</font><br><font color='white'>{centerMessage}</font>");
            }
        }, TimerFlags.REPEAT);
    }

    private void StartTacticalPause()
    {
        _isPauseActive = true;
        if (_pausingTeamScheduled == CsTeam.Terrorist) _team1TacPausesUsed++; else _team2TacPausesUsed++;
        _pausingTeam = _pausingTeamScheduled;
        
        Server.ExecuteCommand("mp_pause_match");
        
        var teamName = GetTeamName((byte)_pausingTeam);
        int pausesUsed = _pausingTeam == CsTeam.Terrorist ? _team1TacPausesUsed : _team2TacPausesUsed;

        Server.PrintToChatAll($"{ChatPrefix} O time {teamName} iniciou uma pausa tática. ({pausesUsed}/{_pluginConfig.PausesTaticoPorEquipe})");
        
        _pauseCountdown = _pluginConfig.DuracaoPauseTatico;
        
        _pauseDisplayTimer?.Kill();
        _pauseDisplayTimer = AddTimer(1.0f, OnPauseTimerTick, TimerFlags.REPEAT);

        _tacTimer?.Kill();
        _tacTimer = AddTimer((float)_pluginConfig.DuracaoPauseTatico, () => UnpauseMatch(isTechPause: false));
    }

    private void OnPauseTimerTick()
    {
        if (_pauseCountdown > 0)
        {
            var teamName = GetTeamName((byte)_pausingTeam!);
            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
            {
                 p.PrintToCenterHtml($"<font color='lightblue'>PAUSA TÁTICA | {teamName}</font><br><font size='6' color='white'>{_pauseCountdown}s</font>");
            }
            _pauseCountdown--;
        }
    }
    
    public void UnpauseMatch(bool isTechPause)
    {
        _pauseDisplayTimer?.Kill();
        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
        {
            p.PrintToCenterHtml(" ");
        }

        if (isTechPause)
        {
            _isTechPauseActive = false;
            _techPauseTeam = CsTeam.None;
            _techPauseScheduledTeam = CsTeam.None;
            Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} despausou a partida!");
        }
        else
        {
            _isPauseActive = false;
            _tacTimer?.Kill();
            Server.PrintToChatAll($"{ChatPrefix} A pausa tática terminou. A partida foi despausada!");
        }
        
        Server.ExecuteCommand("mp_unpause_match");
    }

    [ConsoleCommand("css_r"), ConsoleCommand("css_ready"), ConsoleCommand("css_pronto")]
    public void OnUnifiedReadyCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid || _isMatchLive) return;

        int teamPlayerCount = Utilities.GetPlayers().Count(p => p.TeamNum == player.TeamNum && p.IsValid && !p.IsBot && p.Connected == PlayerConnectedState.PlayerConnected);

        if (teamPlayerCount < 5)
        {
            player.PrintToChat($"{MatchKS.ChatPrefix} Seu time precisa ter pelo menos 5 jogadores para poder dar pronto.");
            return;
        }

        if (player.TeamNum == (byte)CsTeam.Terrorist && !_isTeamTReady)
        {
            _isTeamTReady = true;
            Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Olive}{player.PlayerName}{ChatColors.Default} confirmou o pronto para o time {ChatColors.Gold}TERRORISTA!");
            CheckIfMatchCanStart();
        }
        else if (player.TeamNum == (byte)CsTeam.CounterTerrorist && !_isTeamCTReady)
        {
            _isTeamCTReady = true;
            Server.PrintToChatAll($"{MatchKS.ChatPrefix} {ChatColors.Olive}{player.PlayerName}{ChatColors.Default} confirmou o pronto para o time {ChatColors.LightBlue}CONTRA-TERRORISTA!");
            CheckIfMatchCanStart();
        }
    }



    private void InitiateRestorePause()
    {
        _isTeamTRestoreReady = false;
        _isTeamCTRestoreReady = false;
        _restoreReadyDisplayTimer?.Kill();

        AddTimer(2.0f, () =>
        {
            _isWaitingForRestoreReady = true;
            Server.ExecuteCommand("mp_pause_match");
            Server.PrintToChatAll($"{ChatPrefix} Partida pausada após restore. Os dois times precisam usar {ChatColors.Green}.readyrr{ChatColors.Default} para retomar.");
            _restoreReadyDisplayTimer = AddTimer(1.0f, UpdateRestoreReadyHud, TimerFlags.REPEAT);
        });
    }

    private void UpdateRestoreReadyHud()
    {
        if (!_isWaitingForRestoreReady)
        {
            _restoreReadyDisplayTimer?.Kill();
            return;
        }
        var trColor = _isTeamTRestoreReady ? "green" : "red";
        var ctColor = _isTeamCTRestoreReady ? "green" : "red";
        var trMark = _isTeamTRestoreReady ? "✓" : "?";
        var ctMark = _isTeamCTRestoreReady ? "✓" : "?";
        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
            p.PrintToCenterHtml(
                $"<font color='orange'>RESTORE — CONFIRME PRONTO</font><br>" +
                $"<font color='{trColor}'>TR {trMark}</font> | <font color='{ctColor}'>CT {ctMark}</font><br>" +
                "<font color='white'>.readyrr para confirmar</font>");
    }

    [ConsoleCommand("css_readyrr")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRestoreReadyCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid || !_isWaitingForRestoreReady) return;

        if (AdminManager.PlayerHasPermissions(player, "@css/kick"))
        {
            _isWaitingForRestoreReady = false;
            _restoreReadyDisplayTimer?.Kill();
            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
                p.PrintToCenterHtml(" ");
            Server.ExecuteCommand("mp_unpause_match");
            Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} despausou a partida imediatamente após restore!");
            return;
        }

        bool changed = false;
        if (player.TeamNum == (byte)CsTeam.Terrorist && !_isTeamTRestoreReady)
        {
            _isTeamTRestoreReady = true;
            Server.PrintToChatAll($"{ChatPrefix} Time {ChatColors.Gold}TR{ChatColors.Default} confirmou pronto após restore!");
            changed = true;
        }
        else if (player.TeamNum == (byte)CsTeam.CounterTerrorist && !_isTeamCTRestoreReady)
        {
            _isTeamCTRestoreReady = true;
            Server.PrintToChatAll($"{ChatPrefix} Time {ChatColors.LightBlue}CT{ChatColors.Default} confirmou pronto após restore!");
            changed = true;
        }

        if (!changed) return;

        if (_isTeamTRestoreReady && _isTeamCTRestoreReady)
        {
            _isWaitingForRestoreReady = false;
            _restoreReadyDisplayTimer?.Kill();
            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
                p.PrintToCenterHtml(" ");
            Server.ExecuteCommand("mp_unpause_match");
            Server.PrintToChatAll($"{ChatPrefix} Ambos os times confirmaram! Partida retomada do backup.");
        }
    }
}
