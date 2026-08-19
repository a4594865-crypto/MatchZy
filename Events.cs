using System.Text.Json.Serialization;

namespace MatchZy;

// 使用 .NET 10 主要建構子 (Primary Constructors)，直接在類別宣告參數
public class MatchZyEvent(string eventName)
{
    [JsonPropertyName("event")]
    public string EventName { get; } = eventName;
}

public class MatchZyMatchEvent : MatchZyEvent
{
    [JsonPropertyName("matchid")]
    public required long MatchId { get; init; }

    // 嚴格保留：protected 無法套用主要建構子，維持原寫法保證繼承邏輯不變
    protected MatchZyMatchEvent(string eventName) : base(eventName)
    {
    }
}

public class MatchZyMatchTeamEvent : MatchZyMatchEvent
{
    [JsonPropertyName("team")]
    public required string Team { get; init; }

    protected MatchZyMatchTeamEvent(string eventName) : base(eventName)
    {
    }
}

public class MatchZyMapEvent : MatchZyMatchEvent
{
    [JsonPropertyName("map_number")]
    public required int MapNumber { get; init; }

    protected MatchZyMapEvent(string eventName) : base(eventName)
    {
    }
}

public class MatchZyMapTeamEvent : MatchZyMapEvent
{
    [JsonPropertyName("team_int")]
    public required int TeamNumber { get; init; }

    protected MatchZyMapTeamEvent(string eventName) : base(eventName)
    {
    }
}

public class MatchZyRoundEvent : MatchZyMapEvent
{
    [JsonPropertyName("round_number")]
    public required int RoundNumber { get; init; }

    protected MatchZyRoundEvent(string eventName) : base(eventName)
    {
    }
}

public class MatchZyTimedRoundEvent : MatchZyRoundEvent
{
    [JsonPropertyName("round_time")]
    public required int RoundTime { get; init; }

    protected MatchZyTimedRoundEvent(string eventName) : base(eventName)
    {
    }
}

public class MatchZyPlayerRoundEvent : MatchZyRoundEvent
{
    [JsonPropertyName("player")]
    public required int Player { get; init; }

    protected MatchZyPlayerRoundEvent(string eventName) : base(eventName)
    {
    }
}

public class MatchZyPlayerTimedRoundEvent : MatchZyTimedRoundEvent
{
    [JsonPropertyName("player")]
    public required int Player { get; init; }

    protected MatchZyPlayerTimedRoundEvent(string eventName) : base(eventName)
    {
    }
}

// 🚀 升級：消滅多餘的 { } 括號，將 base("...") 調用直接整合至類別宣告，代碼極致精簡
public class MatchZyPlayerDisconnectedEvent() : MatchZyMatchEvent("player_disconnect")
{
    [JsonPropertyName("player")]
    public required int Player { get; init; }
}

public class MatchZySeriesStartedEvent() : MatchZyMatchEvent("series_start")
{
    [JsonPropertyName("team1")]
    public required MatchZyTeamWrapper Team1 { get; init; }

    [JsonPropertyName("team2")]
    public required MatchZyTeamWrapper Team2 { get; init; }

    [JsonPropertyName("num_maps")]
    public required int NumberOfMaps { get; init; }
}

public class MatchZySeriesResultEvent() : MatchZyMatchEvent("series_end")
{
    [JsonPropertyName("time_until_restore")]
    public required int TimeUntilRestore { get; init; }

    [JsonPropertyName("winner")]
    public required Winner Winner { get; init; }

    [JsonPropertyName("team1_series_score")]
    public required int Team1SeriesScore { get; init; }

    [JsonPropertyName("team2_series_score")]
    public required int Team2SeriesScore { get; init; }
}

public class GoingLiveEvent() : MatchZyMapEvent("going_live")
{
}

public class MatchZyRoundEndedEvent() : MatchZyTimedRoundEvent("round_end")
{
    [JsonPropertyName("reason")]
    public required int Reason { get; init; }

    [JsonPropertyName("winner")]
    public required Winner Winner { get; init; }

    [JsonPropertyName("team1")]
    public required MatchZyStatsTeam StatsTeam1 { get; init; }

    [JsonPropertyName("team2")]
    public required MatchZyStatsTeam StatsTeam2 { get; init; }
}

public class MapResultEvent() : MatchZyMapEvent("map_result")
{
    [JsonPropertyName("winner")]
    public required Winner Winner { get; init; }

    [JsonPropertyName("team1")]
    public required MatchZyStatsTeam StatsTeam1 { get; init; }

    [JsonPropertyName("team2")]
    public required MatchZyStatsTeam StatsTeam2 { get; init; }
}

public class MatchZyMapSelectionEvent : MatchZyMatchTeamEvent
{
    [JsonPropertyName("map_name")]
    public required string MapName { get; init; }

    protected MatchZyMapSelectionEvent(string eventName) : base(eventName)
    {
    }
}

public class MatchZyMapPickedEvent() : MatchZyMapSelectionEvent("map_picked")
{
    [JsonPropertyName("map_number")]
    public required int MapNumber { get; init; }
}

public class MatchZyMapVetoedEvent() : MatchZyMapSelectionEvent("map_vetoed")
{
}

public class MatchZySidePickedEvent() : MatchZyMapSelectionEvent("side_picked")
{
    [JsonPropertyName("map_number")]
    public required int MapNumber { get; init; }

    [JsonPropertyName("side")]
    public required string Side { get; init; }
}

public class MatchZyDemoUploadedEvent() : MatchZyMatchEvent("demo_upload_ended")
{
    [JsonPropertyName("map_number")]
    public required int MapNumber { get; init; }

    [JsonPropertyName("filename")]
    public required string FileName { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }
}
