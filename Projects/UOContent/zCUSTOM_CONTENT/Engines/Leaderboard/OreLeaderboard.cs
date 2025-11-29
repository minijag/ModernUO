using System;
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

    public class OreLeaderboardData
    {
        public DateTime LastUpdated { get; set; }
        public List<OreLeaderboardEntry> Entries { get; set; } = new();
    }

    public static class OreLeaderboard
    {
        // Save in Saves directory so it's automatically included in backups
        private static string SavePath => Path.Combine(World.SavePath, "Data", "ore-leaderboard.json");
        private static List<OreLeaderboardEntry> _cachedLeaderboard = new();
        private static DateTime _lastUpdated = DateTime.MinValue;
        private static TimerExecutionToken _updateTimerToken;

        public static void Configure()
        {
            // Load cached leaderboard from JSON (doesn't require mobiles to be loaded)
            LoadLeaderboard();

            // Subscribe to world load event to update leaderboard after all mobiles are loaded
            EventSink.WorldLoad += EventSink_WorldLoad;

            // Start 24-hour timer using timewheel to recalculate leaderboard
            Timer.StartTimer(TimeSpan.FromHours(1.0), TimeSpan.FromHours(1.0), UpdateLeaderboard, out _updateTimerToken);
        }

        private static void EventSink_WorldLoad()
        {
            // Update leaderboard after all PlayerMobiles are loaded
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
                    .Select(pm => new OreLeaderboardEntry
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
                var data = new OreLeaderboardData
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
                var data = JsonConfig.Deserialize<OreLeaderboardData>(SavePath);
                
                if (data != null)
                {
                    _lastUpdated = data.LastUpdated;
                    _cachedLeaderboard = data.Entries ?? new List<OreLeaderboardEntry>();
                    
                    Console.WriteLine($"[OreLeaderboard] Loaded {_cachedLeaderboard.Count} entries from cache (last updated: {_lastUpdated})");
                }
            }
            catch
            {
                // If file doesn't exist or is corrupted, that's fine - we'll recalculate on world load
                _lastUpdated = DateTime.MinValue;
                _cachedLeaderboard = new List<OreLeaderboardEntry>();
            }
        }
    }
}