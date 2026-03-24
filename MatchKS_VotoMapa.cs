using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Generic;
using System.Linq;

namespace MatchKS;

public partial class MatchKS
{
    // ──────────────────────────────────────────────────────────────
    // Estado do voto de troca de mapa
    // ──────────────────────────────────────────────────────────────
    private bool _votoMapaPendente = false;
    private string _votoMapaNome = "";
    private readonly HashSet<ulong> _votoMapaAceitaram = new();
    private Timer? _votoMapaTimer;
    private Timer? _votoMapaDisplayTimer;
    private int _votoMapaCountdown = 0;

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────
    private List<CCSPlayerController> GetJogadoresElegivelVoto()
    {
        return Utilities.GetPlayers()
            .Where(p => p.IsValid
                && !p.IsBot
                && p.Connected == PlayerConnectedState.PlayerConnected
                && (p.TeamNum == (byte)CsTeam.Terrorist || p.TeamNum == (byte)CsTeam.CounterTerrorist))
            .ToList();
    }

    private void VerificarVotoCompleto()
    {
        if (!_votoMapaPendente) return;

        var elegiveis = GetJogadoresElegivelVoto();
        int total = elegiveis.Count;
        int aceitos = _votoMapaAceitaram.Count(id => elegiveis.Any(p => p.SteamID == id));

        if (total == 0 || aceitos >= total)
            ConfirmarVotoMapa();
    }

    // ──────────────────────────────────────────────────────────────
    // !mudarmapa  — qualquer jogador inicia a votação (somente warmup)
    // ──────────────────────────────────────────────────────────────
    [ConsoleCommand("css_mudarmapa")]
    [CommandHelper(minArgs: 1, usage: "<nome_do_mapa>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnMudarMapaCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_isMatchLive)
        {
            player?.PrintToChat($"{ChatPrefix} Este comando só pode ser usado durante o {ChatColors.Yellow}aquecimento{ChatColors.Default}.");
            return;
        }

        if (_votoMapaPendente)
        {
            player?.PrintToChat($"{ChatPrefix} Já existe uma votação em andamento. Use {ChatColors.Red}!cancelarvoto{ChatColors.Default} para cancelar.");
            return;
        }

        // Sanitizar o nome do mapa para evitar injeção de comandos
        var mapName = command.GetArg(1).Trim()
            .Replace("\"", "")
            .Replace(";", "")
            .Replace("\n", "")
            .Replace("\r", "");

        if (string.IsNullOrWhiteSpace(mapName))
        {
            player?.PrintToChat($"{ChatPrefix} Uso: !mudarmapa <nome_do_mapa>");
            return;
        }

        var elegiveis = GetJogadoresElegivelVoto();

        // Sem jogadores nos times → troca direto
        if (elegiveis.Count == 0)
        {
            Server.PrintToChatAll($"{ChatPrefix} Trocando o mapa para {ChatColors.Green}{mapName}{ChatColors.Default}...");
            AddTimer(1.5f, () => Server.ExecuteCommand($"changelevel \"{mapName}\""));
            return;
        }

        _votoMapaPendente = true;
        _votoMapaNome = mapName;
        _votoMapaAceitaram.Clear();
        _votoMapaCountdown = 60;

        var quemPediu = player?.PlayerName ?? "Console";
        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Gold}{quemPediu}{ChatColors.Default} quer trocar o mapa para {ChatColors.Green}{mapName}{ChatColors.Default}!");
        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Yellow}TODOS{ChatColors.Default} os jogadores precisam aceitar. Digite {ChatColors.Green}!aceitar{ChatColors.Default} ou {ChatColors.Red}!recusar{ChatColors.Default}. ({elegiveis.Count} jogador(es))");

        _votoMapaDisplayTimer?.Kill();
        _votoMapaDisplayTimer = AddTimer(1.0f, AtualizarHudVotoMapa, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

        _votoMapaTimer?.Kill();
        _votoMapaTimer = AddTimer(60.0f, () =>
        {
            if (!_votoMapaPendente) return;
            CancelarVotoMapa("Tempo esgotado");
        });
    }

    // ──────────────────────────────────────────────────────────────
    // !cancelarvoto  — qualquer jogador cancela a própria votação (ou qualquer)
    // ──────────────────────────────────────────────────────────────
    [ConsoleCommand("css_cancelarvoto")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnCancelarVotoCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!_votoMapaPendente)
        {
            player?.PrintToChat($"{ChatPrefix} Não há nenhuma votação ativa para cancelar.");
            return;
        }

        CancelarVotoMapa($"{ChatColors.Red}cancelada pelo admin{ChatColors.Default}");
    }

    // ──────────────────────────────────────────────────────────────
    // HUD de countdown
    // ──────────────────────────────────────────────────────────────
    private void AtualizarHudVotoMapa()
    {
        if (!_votoMapaPendente)
        {
            _votoMapaDisplayTimer?.Kill();
            return;
        }

        var elegiveis = GetJogadoresElegivelVoto();
        int total = elegiveis.Count;
        int aceitos = _votoMapaAceitaram.Count(id => elegiveis.Any(p => p.SteamID == id));

        // Alguém desconectou e agora todos restantes já aceitaram
        if (total == 0 || aceitos >= total)
        {
            ConfirmarVotoMapa();
            return;
        }

        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
        {
            bool jaAceitou = _votoMapaAceitaram.Contains(p.SteamID);
            bool eElegivel = p.TeamNum == (byte)CsTeam.Terrorist || p.TeamNum == (byte)CsTeam.CounterTerrorist;

            string statusPessoal;
            if (!eElegivel)
                statusPessoal = "<font color='gray'>Espectador — sem voto</font>";
            else if (jaAceitou)
                statusPessoal = "<font color='green'>✓ Você aceitou</font>";
            else
                statusPessoal = "<font color='yellow'>!aceitar</font>  <font color='red'>!recusar</font>";

            p.PrintToCenterHtml(
                // Adiciona varias quebras de linha para descer o HUD e nao bater com o AQUECIMENTO
                "<br><br><br><br><br><br><br><br><br>" +
                $"<font color='orange'>VOTAÇÃO — TROCA DE MAPA</font><br>" +
                $"<font color='white'>Mapa: </font><font color='lime'>{_votoMapaNome}</font><br>" +
                $"<font color='lightblue'>{aceitos}/{total}</font><font color='white'> aceitaram</font>" +
                $"  <font color='yellow'>{_votoMapaCountdown}s</font><br>" +
                $"{statusPessoal}"
            );
        }

        if (_votoMapaCountdown > 0)
            _votoMapaCountdown--;
    }

    // ──────────────────────────────────────────────────────────────
    // !aceitar
    // ──────────────────────────────────────────────────────────────
    [ConsoleCommand("css_aceitar")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnAceitarVotoMapaCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        if (!_votoMapaPendente)
        {
            player.PrintToChat($"{ChatPrefix} Não há nenhuma votação ativa no momento.");
            return;
        }

        if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist)
        {
            player.PrintToChat($"{ChatPrefix} Apenas jogadores nos times (CT ou TR) podem votar.");
            return;
        }

        if (_votoMapaAceitaram.Contains(player.SteamID))
        {
            player.PrintToChat($"{ChatPrefix} Você já aceitou esta troca de mapa.");
            return;
        }

        _votoMapaAceitaram.Add(player.SteamID);

        var elegiveis = GetJogadoresElegivelVoto();
        int aceitos = _votoMapaAceitaram.Count(id => elegiveis.Any(p => p.SteamID == id));
        int total = elegiveis.Count;

        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Green}{player.PlayerName}{ChatColors.Default} aceitou. ({aceitos}/{total})");

        if (aceitos >= total)
            ConfirmarVotoMapa();
    }

    // ──────────────────────────────────────────────────────────────
    // !recusar
    // ──────────────────────────────────────────────────────────────
    [ConsoleCommand("css_recusar")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRecusarVotoMapaCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;

        if (!_votoMapaPendente)
        {
            player.PrintToChat($"{ChatPrefix} Não há nenhuma votação ativa no momento.");
            return;
        }

        if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist)
        {
            player.PrintToChat($"{ChatPrefix} Apenas jogadores nos times (CT ou TR) podem votar.");
            return;
        }

        CancelarVotoMapa($"{ChatColors.Red}{player.PlayerName}{ChatColors.Default} recusou");
    }

    // ──────────────────────────────────────────────────────────────
    // Confirmação e cancelamento
    // ──────────────────────────────────────────────────────────────
    private void ConfirmarVotoMapa()
    {
        _votoMapaTimer?.Kill();
        _votoMapaDisplayTimer?.Kill();
        _votoMapaPendente = false;

        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
            p.PrintToCenterHtml(" ");

        Server.PrintToChatAll($"{ChatPrefix} {ChatColors.Green}Todos aceitaram!{ChatColors.Default} Trocando para {ChatColors.Lime}{_votoMapaNome}{ChatColors.Default} em 5 segundos...");
        AddTimer(5.0f, () => Server.ExecuteCommand($"changelevel \"{_votoMapaNome}\""));
    }

    internal void CancelarVotoMapa(string motivo)
    {
        _votoMapaTimer?.Kill();
        _votoMapaDisplayTimer?.Kill();
        _votoMapaPendente = false;
        _votoMapaAceitaram.Clear();

        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid))
            p.PrintToCenterHtml(" ");

        Server.PrintToChatAll($"{ChatPrefix} Votação de troca de mapa cancelada: {motivo}.");
    }
}
