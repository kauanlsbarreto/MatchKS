using System.Collections.Generic;

namespace MatchKS;

public class MatchConfig
{
    public MatchTeam Team1 { get; set; } = new();
    public MatchTeam Team2 { get; set; } = new();
    public List<MapConfig> MapList { get; set; } = new();
    public int CurrentMapIndex { get; set; } = 0;
    public string Hostname { get; set; } = "MatchKS Match";
    public bool EnableFriendlyFire { get; set; } = false;
    public string GameMode { get; set; } = "Competitive";
    public bool EnableOvertime { get; set; } = true;
}

public class MatchTeam
{
    public string Name { get; set; } = "";
    public string Tag { get; set; } = "";
    public Dictionary<string, string> Players { get; set; } = new();
    public int MapsWon { get; set; } = 0;
}

public class MapConfig
{
    public string Name { get; set; } = "";
    public bool EnableKnifeRound { get; set; } = true;
}

public class PluginConfig
{
    public int PausesTaticoPorEquipe { get; set; } = 4;
    public int DuracaoPauseTatico { get; set; } = 30;
    public bool RoundFaca { get; set; } = true;
    public bool FogoAmigo { get; set; } = false;
    public bool EnableOvertime { get; set; } = true;
    public int OvertimeStartMoney { get; set; } = 10000;
    public string DemoNameFormat { get; set; } = "{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}";
    public string DemoFolderPath { get; set; } = "matchksDEMOS/";
    public bool DiscordWebhookEnabled { get; set; } = true;
    public string DiscordWebhookUrl { get; set; } = "https://discord.com/api/webhooks/1482129508742992052/X2U0Yq_gA3lBrotz40tl2evBNrOusZ7SiZ6LwmrQZ2-GcTZ-FL-KHSRjhZD8r5xEOyV-";
}

public class TeamNameOwnerConfig
{
    public Dictionary<string, string> SteamIdToTeamName { get; set; } = new();
}

public class BackupSessionInfo
{
    public Dictionary<string, string> Team1Players { get; set; } = new();
    public Dictionary<string, string> Team2Players { get; set; } = new();
    public string Team1Name { get; set; } = "";
    public string Team2Name { get; set; } = "";
    public string MapName { get; set; } = "";
    public int LastBackupRound { get; set; } = 0;
    public bool MatchEnded { get; set; } = false;
}
