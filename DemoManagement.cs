using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;

namespace MatchZy
{
    public partial class MatchZy
    {
        // 【0-GC 快取區】：快取 GOTV 相關原生 ConVar 指標，免除重複跨 C++/C# 查找
        private ConVar? _cachedTvEnableCvar;
        private ConVar? _cachedTvEnable1Cvar;
        private ConVar? _cachedTvDelayCvar;
        private ConVar? _cachedTvDelay1Cvar;

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
                // 【0-GC 優化】：直接使用多參數 Path.Join，消除 Server.GameDirectory + "/csgo/" 產生的中間暫存字串
                string? directoryPath = Path.GetDirectoryName(Path.Join(Server.GameDirectory, "csgo", demoPath));
                
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
            // 【0-GC 優化】：消除多餘的 + "/csgo/" 中間字串配置
            string fullDemoPath = Path.Join(Server.GameDirectory, "csgo", activeDemoFile);
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
                    string uploadUrl = demoUploadURL;
                    string headerKey = demoUploadHeaderKey;
                    string headerVal = demoUploadHeaderValue;

                    Task.Run(async () =>
                    {
                        await UploadFileAsync(fullDemoPath, uploadUrl, headerKey, headerVal, liveMatchId, currentMapNumber, roundNumber);
                    });
                });
            });
        }

        public int GetTvDelay()
        {
            // 【0-GC 優化】：快取 ConVar 指標，徹底拆除強制轉換驚嘆號 (!)，杜絕 CS8602 潛在空參考例外與重複查找
            _cachedTvEnableCvar ??= ConVar.Find("tv_enable");
            if (_cachedTvEnableCvar is null || !_cachedTvEnableCvar.GetPrimitiveValue<bool>()) return 0;

            _cachedTvEnable1Cvar ??= ConVar.Find("tv_enable1");
            bool tvEnable1 = _cachedTvEnable1Cvar is not null && _cachedTvEnable1Cvar.GetPrimitiveValue<bool>();

            _cachedTvDelayCvar ??= ConVar.Find("tv_delay");
            int tvDelay = _cachedTvDelayCvar is not null ? _cachedTvDelayCvar.GetPrimitiveValue<int>() : 0;

            if (!tvEnable1) return tvDelay;

            _cachedTvDelay1Cvar ??= ConVar.Find("tv_delay1");
            int tvDelay1 = _cachedTvDelay1Cvar is not null ? _cachedTvDelay1Cvar.GetPrimitiveValue<int>() : 0;

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
