using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Exoplanet.DAL;
using Exoplanet.Services;
using Microsoft.EntityFrameworkCore;

namespace Exoplanet.Services;

public sealed class ExoplanetService : IExoplanetService
{
    private readonly IExoplanetApiClient _api;
    private readonly ExoplanetDbContext _db;

    public ExoplanetService(IExoplanetApiClient api, ExoplanetDbContext db)
    {
        _api = api;
        _db = db;
    }

    public async Task<ExoplanetRunResult> RunAsync(CancellationToken ct)
    {
        int fetched = 0, inserted = 0, updated = 0, skipped = 0;

        const int batchSize = 500;
        var batch = new List<(PsCompParsKeys keys, string payloadJson, string hash)>(batchSize);
        var elements = _api.FetchPsCompParsAsync(ct);

        await foreach (var element in elements)
        {
            fetched++;

            var payloadJson = element.GetRawText();
            var keys = JsonSerializer.Deserialize<PsCompParsKeys>(payloadJson) ?? new PsCompParsKeys();

            // Strong opinion: if we don't have stable keys, skip (or send to a rejects table later)
            if (string.IsNullOrWhiteSpace(keys.PlName) || string.IsNullOrWhiteSpace(keys.Hostname))
            {
                skipped++;
                continue;
            }

            var hash = Sha256Hex(NormalizeJson(payloadJson));
            batch.Add((keys, payloadJson, hash));

            if (batch.Count >= batchSize)
            {
                var (i, u, s) = await UpsertBatchAsync(batch, ct);
                inserted += i; updated += u; skipped += s;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            var (i, u, s) = await UpsertBatchAsync(batch, ct);
            inserted += i; updated += u; skipped += s;
        }

        return new ExoplanetRunResult(fetched, inserted, updated, skipped);
    }

    private async Task<(int inserted, int updated, int skipped)> UpsertBatchAsync(
        List<(PsCompParsKeys keys, string payloadJson, string hash)> batch,
        CancellationToken ct)
    {
        int inserted = 0, updated = 0, skipped = 0;

        var distinctKeys = batch
            .Select(x => new { Pl = x.keys.PlName!, Host = x.keys.Hostname! })
            .Distinct()
            .ToList();

        // NOTE: this "Any" pattern is simple but not the fastest for large batches.
        // If we want performance, we’ll switch to ON CONFLICT DO UPDATE via raw SQL.
        var existing = await _db.ExoplanetRaw
            .Where(r => distinctKeys.Any(k => k.Pl == r.PlName && k.Host == r.Hostname))
            .ToListAsync(ct);

        var map = existing.ToDictionary(x => (x.PlName, x.Hostname), x => x);

        foreach (var item in batch)
        {
            var pl = item.keys.PlName!;
            var host = item.keys.Hostname!;

            if (!map.TryGetValue((pl, host), out var row))
            {
                _db.ExoplanetRaw.Add(new ExoplanetRaw
                {
                    PlName = pl,
                    Hostname = host,
                    DiscYear = item.keys.DiscYear,
                    RowHash = item.hash,
                    IngestedAtUtc = DateTime.UtcNow,
                    PayloadJson = item.payloadJson
                });
                inserted++;
            }
            else
            {
                if (row.RowHash == item.hash)
                {
                    skipped++;
                    continue;
                }

                row.DiscYear = item.keys.DiscYear;
                row.RowHash = item.hash;
                row.IngestedAtUtc = DateTime.UtcNow;
                row.PayloadJson = item.payloadJson;
                updated++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return (inserted, updated, skipped);
    }

    private static string NormalizeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement);
    }

    private static string Sha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
