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
        var team1 = SanitizeFileName(string.IsNullOrWhiteSpace(_activeMatch.Team1.Name) ? "Terroristas" : _activeMatch.Team1.Name).Replace(" ", "_");
        var team2 = SanitizeFileName(string.IsNullOrWhiteSpace(_activeMatch.Team2.Name) ? "Contra-Terroristas" : _activeMatch.Team2.Name).Replace(" ", "_");
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

        var t1 = SanitizeFileName(_activeMatch?.Team1.Name ?? "TR").Replace(" ", "_");
        var t2 = SanitizeFileName(_activeMatch?.Team2.Name ?? "CT").Replace(" ", "_");

        var fileName = $"round_{round:00}_{t1}_vs_{t2}_{DateTime.Now:yyyyMMdd_HHmmss}.cfg";
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
        if (!_isMatchLive || player == null || !player.IsValid) return;

        if (_isTechPauseActive)
        {
            bool isAdmin = AdminManager.PlayerHasPermissions(player, "@css/kick");
            bool isPauser = player.SteamID == _techPausePauserSteamId;

            if (isAdmin || isPauser)
            {
                UnpauseMatch(isTechPause: true);
            }
            else
            {
                player.PrintToChat($"{ChatPrefix} Apenas {ChatColors.Green}{_techPausePauserName}{ChatColors.Default} ou um {ChatColors.Red}ADMIN{ChatColors.Default} pode remover a pausa.");
                player.PrintToChat($"{ChatPrefix} Alternativa: Ambos os times podem digitar {ChatColors.Green}.live{ChatColors.Default}.");
            }
            return;
        }

        if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist)
        {
            player.PrintToChat($"{ChatPrefix} Você precisa estar em um time para pedir pausa.");
            return;
        }

        _isTechPauseActive = true;
        _techPausePauserSteamId = player.SteamID;
        _techPausePauserName = player.PlayerName;
        _techPauseVoteTR = false;
        _techPauseVoteCT = false;

        Server.ExecuteCommand("mp_pause_match");
        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Green}{player.PlayerName}{ChatColors.Default} iniciou uma {ChatColors.Red}PAUSA TÉCNICA{ChatColors.Default}.");
        Server.PrintToChatAll($"{ChatPrefix} Digite {ChatColors.Green}.tec{ChatColors.Default} novamente para despausar (somente quem pausou).");
        Server.PrintToChatAll($"{ChatPrefix} Ou ambos os times digitem {ChatColors.Green}.live{ChatColors.Default} para seguir.");

        _pauseDisplayTimer?.Kill();
        _pauseDisplayTimer = AddTimer(1.0f, () =>
        {
            var trStatus = _techPauseVoteTR ? "<font color='green'>PRONTO</font>" : "<font color='red'>AGUARDANDO</font>";
            var ctStatus = _techPauseVoteCT ? "<font color='green'>PRONTO</font>" : "<font color='red'>AGUARDANDO</font>";

            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
            {
                p.PrintToCenterHtml(
                    $"<font color='orange'>PAUSA TÉCNICA</font><br>" +
                    $"<font color='white'>Pausado por: {_techPausePauserName}</font><br><br>" +
                    $"<font color='white'>TR: {trStatus} <font color='white'> vs </font> CT: {ctStatus}</font><br>" +
                    $"<font size='12'>Digite .live para confirmar</font>"
                );
            }
        }, TimerFlags.REPEAT);
    }
    
    [ConsoleCommand("css_live")]
    public void OnLiveCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_isTechPauseActive || player == null || !player.IsValid) return;

        bool changed = false;
        if (player.TeamNum == (byte)CsTeam.Terrorist && !_techPauseVoteTR)
        {
            _techPauseVoteTR = true;
            Server.PrintToChatAll($"{ChatPrefix} Time {ChatColors.Gold}TR{ChatColors.Default} está pronto para voltar (.live).");
            changed = true;
        }
        else if (player.TeamNum == (byte)CsTeam.CounterTerrorist && !_techPauseVoteCT)
        {
            _techPauseVoteCT = true;
            Server.PrintToChatAll($"{ChatPrefix} Time {ChatColors.LightBlue}CT{ChatColors.Default} está pronto para voltar (.live).");
            changed = true;
        }

        if (changed && _techPauseVoteTR && _techPauseVoteCT)
        {
            Server.PrintToChatAll($"{ChatPrefix} Ambos os times concordaram em despausar!");
            UnpauseMatch(isTechPause: true);
        }
    }

    public void UnpauseMatch(bool isTechPause = true)
    {
        _pauseDisplayTimer?.Kill();
        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
        {
            p.PrintToCenterHtml(" ");
        }

        _isTechPauseActive = false;
        _techPausePauserSteamId = 0;
        _techPauseVoteTR = false;
        _techPauseVoteCT = false;
        
        Server.PrintToChatAll($"{ChatPrefix} A partida foi despausada!");
        
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

    // ──────────────────────────────────────────────────────────────
    // SISTEMA AVANÇADO DE BACKUP MANUAL (backup1 / restore1)
    // ──────────────────────────────────────────────────────────────

    [ConsoleCommand("css_backup1")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/kick")]
    public void OnBackup1Command(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;
        var rootPath = GetBackupRootPath();

        // Se não existir pasta de backups
        if (!Directory.Exists(rootPath))
        {
            player.PrintToChat($"{ChatPrefix} Nenhuma pasta de backup encontrada em {rootPath}.");
            return;
        }

        var directories = Directory.GetDirectories(rootPath).OrderByDescending(d => Directory.GetLastWriteTime(d)).ToList();
        
        // Se o admin forneceu um argumento (índice da pasta)
        if (command.ArgCount > 1 && int.TryParse(command.GetArg(1), out int index))
        {
            if (index < 1 || index > directories.Count)
            {
                player.PrintToChat($"{ChatPrefix} Índice inválido. Use .backup1 para ver a lista.");
                return;
            }

            var selectedFolder = directories[index - 1];
            _adminSelectedBackupFolder[player.SteamID] = selectedFolder;
            
            var folderName = Path.GetFileName(selectedFolder);
            player.PrintToChat($"{ChatPrefix} Pasta selecionada: {ChatColors.Green}{folderName}");
            player.PrintToChat($"{ChatPrefix} Agora use {ChatColors.Green}.restore1 <round>{ChatColors.Default} para ver os arquivos.");
            return;
        }

        // Lista as pastas disponíveis
        player.PrintToChat($"{ChatPrefix} {ChatColors.Yellow}Pastas de Backup Disponíveis:{ChatColors.Default}");
        int i = 1;
        foreach (var dir in directories.Take(10)) // Mostra as 10 mais recentes
        {
            var dirName = Path.GetFileName(dir);
            var date = Directory.GetLastWriteTime(dir).ToString("dd/MM HH:mm");
            player.PrintToChat($" {ChatColors.Green}{i}.{ChatColors.Default} {dirName} [{date}]");
            i++;
        }
        player.PrintToChat($"{ChatPrefix} Uso: {ChatColors.Green}.backup1 <numero>{ChatColors.Default} para selecionar.");
    }

    [ConsoleCommand("css_restore1")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/kick")]
    public void OnRestore1Command(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null) return;

        // Verifica se o admin já selecionou uma pasta
        if (!_adminSelectedBackupFolder.TryGetValue(player.SteamID, out var selectedFolder) || !Directory.Exists(selectedFolder))
        {
            player.PrintToChat($"{ChatPrefix} Nenhuma pasta selecionada ou pasta não existe mais.");
            player.PrintToChat($"{ChatPrefix} Use {ChatColors.Green}.backup1{ChatColors.Default} primeiro para escolher a partida.");
            return;
        }

        var folderName = Path.GetFileName(selectedFolder);
        var backups = GetBackupsFromDirectory(selectedFolder);

        if (backups.Count == 0)
        {
            player.PrintToChat($"{ChatPrefix} A pasta {ChatColors.Red}{folderName}{ChatColors.Default} está vazia.");
            return;
        }

        // Se forneceu o round, tenta restaurar
        if (command.ArgCount > 1 && int.TryParse(command.GetArg(1), out int roundNum))
        {
            var targetBackup = backups.FirstOrDefault(b => b.Round == roundNum);
            
            if (targetBackup == null)
            {
                player.PrintToChat($"{ChatPrefix} Round {roundNum} não encontrado na pasta {folderName}.");
                return;
            }

            if (!_isMatchLive)
            {
                player.PrintToChat($"{ChatPrefix} Para restaurar, a partida precisa estar iniciada (.start ou .ready).");
                return;
            }

            var relativePath = ToCsgoRelativePath(CsgoRootPath, targetBackup.AbsolutePath);
            Server.ExecuteCommand($"mp_backup_restore_load_file \"{relativePath}\"");
            
            Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Red}ADMIN{ChatColors.Default} restaurou backup manual.");
            Server.PrintToChatAll($"{ChatPrefix} Pasta: {folderName} | Round: {roundNum}");
            InitiateRestorePause();
            return;
        }

        // Lista os rounds disponíveis na pasta selecionada
        player.PrintToChat($"{ChatPrefix} Backups em: {ChatColors.Green}{folderName}{ChatColors.Default}");
        var roundsList = string.Join(", ", backups.Select(b => b.Round));
        player.PrintToChat($"{ChatPrefix} Rounds: {ChatColors.Yellow}{roundsList}");
        player.PrintToChat($"{ChatPrefix} Uso: {ChatColors.Green}.restore1 <round>{ChatColors.Default} para carregar.");
    }
}
