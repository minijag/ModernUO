/* using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Server.Mobiles;
using Server.Json;

namespace Server.Engines.Custom
{
    public class OreLeaderboardEntry
    {
        public string PlayerName { get; set; }
        public int TotalOreMined { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public static class OreLeaderboard
    {
        private static readonly string SavePath = Path.Combine(Core.BaseDirectory, "Data", "ore-leaderboard.json");
        private static List<OreLeaderboardEntry> _cachedLeaderboard = new();
        private static DateTime _lastUpdated = DateTime.MinValue;
        private static Timer _updateTimer;
        // private static TimerExecutionToken _updateTimerToken;

        public static void Configure()
        {
            LoadLeaderboard();

            // Start timer to recalculate every 24 hours
            _updateTimer = new Timer(TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(5.0))
            {
                
            };
            _updateTimer.Start();
            //Timer.StartTimer(TimeSpan.FromHours(24.0), TimeSpan.FromHours(24.0), UpdateLeaderboard, out _updateTimerToken);

            // Also recalculate on startup
            UpdateLeaderboard();
        }

        public static List<OreLeaderboardEntry> GetLeaderboard(int topCount = 10)
        {
            return _cachedLeaderboard.Take(topCount).ToList();
        }

        private static void UpdateLeaderboard()
        {
            try
            {
                var leaderboard = World.Mobiles.Values
                    .OfType<PlayerMobile>()
                    .Where(pm => pm.TotalOreMined > 0)
                    .OrderByDescending(pm => pm.TotalOreMined)
                    .Take(10)
                    .Select((pm, index) => new OreLeaderboardEntry
                    {
                        PlayerName = pm.Name ?? "Unknown",
                        TotalOreMined = pm.TotalOreMined,
                        LastUpdated = Core.Now
                    })
                    .ToList();

                _cachedLeaderboard = leaderboard;
                _lastUpdated = Core.Now;

                SaveLeaderboard();

                Console.WriteLine($"[OreLeaderboard] Updated at {Core.Now}: {leaderboard.Count} entries");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OreLeaderboard] Error updating leaderboard: {ex.Message}");
            }
        }

        private static void SaveLeaderboard()
        {
            try
            {
                var data = new
                {
                    LastUpdated = _lastUpdated,
                    Entries = _cachedLeaderboard
                };

                var directory = Path.GetDirectoryName(SavePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                JsonConfig.Serialize(SavePath, data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OreLeaderboard] Error saving leaderboard: {ex.Message}");
            }
        }

        private static void LoadLeaderboard()
        {
            try
            {
                var data = JsonConfig.Deserialize<Dictionary<string, object>>(SavePath);
                
                if (data != null && data.ContainsKey("LastUpdated"))
                {
                    if (DateTime.TryParse(data["LastUpdated"].ToString(), out var lastUpdated))
                    {
                        _lastUpdated = lastUpdated;
                    }

                    // Try to load entries (JSON structure might vary)
                    // If load fails, we'll just recalculate
                }
            }
            catch
            {
                // If file doesn't exist or is corrupted, that's fine - we'll recalculate
                _lastUpdated = DateTime.MinValue;
            }
        }
    }
}
 */