using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;

namespace MatchZy
{
    public partial class MatchZy
    {
        public string demoPath = "MatchZy/";
        public string demoNameFormat = "{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}";
        public string demoUploadURL = "";
        public string demoUploadHeaderKey = "";
        public string demoUploadHeaderValue = "";

        public string activeDemoFile = "";

        public bool isDemoRecording = false;
        public bool isDemoRecordingEnabled = true;

        public void StartDemoRecording()
        {
            if (!isDemoRecordingEnabled)
            {
                Log("[StartDemoRecording] Demo recording is disabled.");
                return;
            }
            if (isDemoRecording)
            {
                Log("[StartDemoRecording] Demo recording is already in progress.");
                return;
            }
            string demoFileName = FormatCvarValue(demoNameFormat.Replace(" ", "_")) + ".dem";
            try
            {
                string? directoryPath = Path.GetDirectoryName(Path.Join(Server.GameDirectory + "/csgo/", demoPath));
                
                // 合併檢查，屬性模式匹配確保資料夾路徑不為空
                if (directoryPath is not null && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                
                // .NET 10 模式匹配，取代傳統 == ""
                string tempDemoPath = demoPath is "" ? demoFileName : demoPath + demoFileName;
                activeDemoFile = tempDemoPath;
                
                Log($"[StartDemoRecoding] Starting demo recording, path: {tempDemoPath}");
                Server.ExecuteCommand($"tv_record {tempDemoPath}");
                isDemoRecording = true;
            }
            catch (Exception ex)
            {
                Log($"[StartDemoRecording - FATAL] Error: {ex.Message}. Starting demo recording with path. Name: {demoFileName}");
                // This is to avoid demo loss in any case of exception
                Server.ExecuteCommand($"tv_record {demoFileName}");
                isDemoRecording = true;
            }
        }

        public void StopDemoRecording(float delay, string activeDemoFile, long liveMatchId, int currentMapNumber)
        {
            Log($"[StopDemoRecording] Going to stop demorecording in {delay}s");
            string demoPath = Path.Join(Server.GameDirectory + "/csgo/", activeDemoFile);
            (int t1score, int t2score) = GetTeamsScore();
            int roundNumber = t1score + t2score;
            
            AddTimer(delay, () =>
            {
                if (isDemoRecording)
                {
                    Server.ExecuteCommand($"tv_stoprecord");
                }
                isDemoRecording = false;
                
                AddTimer(15, () =>
                {
                    Task.Run(async () =>
                    {
                        await UploadFileAsync(demoPath, demoUploadURL, demoUploadHeaderKey, demoUploadHeaderValue, liveMatchId, currentMapNumber, roundNumber);
                    });
                });
            });
        }

        public int GetTvDelay()
        {
            // 徹底拆除強制轉換驚嘆號 (!)，嚴格檢查 CVar 是否存在，杜絕 CS8602 潛在空參考例外
            var tvEnableCvar = ConVar.Find("tv_enable");
            if (tvEnableCvar is null || !tvEnableCvar.GetPrimitiveValue<bool>()) return 0;

            var tvEnable1Cvar = ConVar.Find("tv_enable1");
            bool tvEnable1 = tvEnable1Cvar is not null && tvEnable1Cvar.GetPrimitiveValue<bool>();

            var tvDelayCvar = ConVar.Find("tv_delay");
            int tvDelay = tvDelayCvar is not null ? tvDelayCvar.GetPrimitiveValue<int>() : 0;

            if (!tvEnable1) return tvDelay;

            var tvDelay1Cvar = ConVar.Find("tv_delay1");
            int tvDelay1 = tvDelay1Cvar is not null ? tvDelay1Cvar.GetPrimitiveValue<int>() : 0;

            return tvDelay < tvDelay1 ? tvDelay1 : tvDelay;
        }

        [ConsoleCommand("get5_demo_upload_header_key", "If defined, a custom HTTP header with this name is added to the HTTP requests for demos")]
        [ConsoleCommand("matchzy_demo_upload_header_key", "If defined, a custom HTTP header with this name is added to the HTTP requests for demos")]
        public void DemoUploadHeaderKeyCommand(CCSPlayerController? player, CommandInfo command)
        {
            // 改用 is not null
            if (player is not null) return;
            string header = command.ArgByIndex(1).Trim();

            // .NET 10 常數模式匹配
            if (header is not "") demoUploadHeaderKey = header;
        }

        [ConsoleCommand("get5_demo_upload_header_value", "If defined, the value of the custom header added to the demos sent over HTTP")]
        [ConsoleCommand("matchzy_demo_upload_header_value", "If defined, the value of the custom header added to the demos sent over HTTP")]
        public void DemoUploadHeaderValueCommand(CCSPlayerController? player, CommandInfo command)
        {
            // 改用 is not null
            if (player is not null) return;
            string headerValue = command.ArgByIndex(1).Trim();

            // .NET 10 常數模式匹配
            if (headerValue is not "") demoUploadHeaderValue = headerValue;
        }
    }
}
