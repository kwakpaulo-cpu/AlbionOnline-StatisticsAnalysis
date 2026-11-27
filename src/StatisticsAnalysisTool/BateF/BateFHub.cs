using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.BateF;

// Bate F: envia o loot e as kills registrados pelo Loot Logger para o Bate F Hub, ao vivo.
public static class BateFHub
{
    public const string HubUrl = "https://corre-bate-f.batef.workers.dev";
    public const bool DisableUpstreamUpdates = true;
    private const string TokenFileName = "BateF-token.txt";
    private const int MaxQueue = 20000;

    private static readonly ConcurrentQueue<object[]> LootQueue = new();
    private static readonly ConcurrentQueue<object[]> KillQueue = new();
    private static readonly ConcurrentDictionary<string, double> Stats = new();
    private static string _playerName = string.Empty;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static int _started;

    public static string Status { get; private set; } = string.Empty;

    public static string ReadToken()
    {
        try
        {
            var paths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, TokenFileName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), TokenFileName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", TokenFileName)
            };
            foreach (var path in paths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var token = File.ReadAllText(path).Trim();
                if (token.Length > 0)
                {
                    return token;
                }
            }
        }
        catch
        {
            // ignorado
        }

        return null;
    }

    private static long ToUnixMs(DateTime utc)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    }

    public static void EnqueueLoot(DateTime utc, string lootedBy, string lootedByGuild, string itemId, string itemName,
        long quantity, long unitValue, string lootedFrom, string lootedFromGuild)
    {
        if (LootQueue.Count >= MaxQueue)
        {
            return;
        }

        LootQueue.Enqueue([
            ToUnixMs(utc), lootedBy ?? string.Empty, lootedByGuild ?? string.Empty, itemId ?? string.Empty,
            string.IsNullOrEmpty(itemName) ? itemId ?? string.Empty : itemName, quantity, unitValue,
            lootedFrom ?? string.Empty, lootedFromGuild ?? string.Empty
        ]);
        Start();
    }

    public static void EnqueueKill(DateTime utc, string died, string diedGuild, string killedBy, string killedByGuild)
    {
        if (KillQueue.Count >= MaxQueue)
        {
            return;
        }

        KillQueue.Enqueue([ToUnixMs(utc), died ?? string.Empty, diedGuild ?? string.Empty, killedBy ?? string.Empty, killedByGuild ?? string.Empty]);
        Start();
    }

    public static void AddStat(string playerName, string valueType, double value)
    {
        if (string.IsNullOrEmpty(valueType) || !double.IsFinite(value) || value == 0)
        {
            return;
        }

        if (!string.IsNullOrEmpty(playerName))
        {
            _playerName = playerName;
        }

        Stats.AddOrUpdate(valueType, value, (_, old) => old + value);
        Start();
    }

    private static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _ = Task.Run(LoopAsync);
    }

    private static async Task LoopAsync()
    {
        while (true)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                await FlushAsync();
            }
            catch
            {
                // continua tentando
            }
        }
    }

    private static async Task FlushAsync()
    {
        if (LootQueue.IsEmpty && KillQueue.IsEmpty && Stats.IsEmpty)
        {
            return;
        }

        var token = ReadToken();
        if (string.IsNullOrEmpty(token))
        {
            Status = "Sem codigo do Bate F Hub";
            return;
        }

        var loot = new List<object[]>();
        while (loot.Count < 2000 && LootQueue.TryDequeue(out var row))
        {
            loot.Add(row);
        }

        var kills = new List<object[]>();
        while (kills.Count < 500 && KillQueue.TryDequeue(out var row))
        {
            kills.Add(row);
        }

        var stats = new Dictionary<string, double>();
        foreach (var key in Stats.Keys)
        {
            if (Stats.TryRemove(key, out var value))
            {
                stats[key] = value;
            }
        }

        var body = JsonSerializer.Serialize(new { loot, kills, stats, player = _playerName });
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, HubUrl + "/api/live");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.Add("x-batef-token", token);
            using var response = await Client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                Status = "Enviando para o Bate F Hub";
                return;
            }

            Status = (int) response.StatusCode == 401 ? "Codigo do Bate F Hub invalido" : "Bate F Hub respondeu " + (int) response.StatusCode;
            if ((int) response.StatusCode == 401)
            {
                return;
            }
        }
        catch
        {
            Status = "Sem conexao com o Bate F Hub";
        }

        foreach (var row in loot)
        {
            LootQueue.Enqueue(row);
        }

        foreach (var row in kills)
        {
            KillQueue.Enqueue(row);
        }

        foreach (var pair in stats)
        {
            Stats.AddOrUpdate(pair.Key, pair.Value, (_, old) => old + pair.Value);
        }
    }
}
