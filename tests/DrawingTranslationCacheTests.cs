using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using StructuralToolbox.DrawingTranslation.Api;

internal static class DrawingTranslationCacheTests
{
    internal static void Run()
    {
        string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "cache-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckDamagedEntries(Path.Combine(directory, "damaged.json"));
            CheckCapacity(Path.Combine(directory, "capacity.json"));
            CheckBrokenJson(Path.Combine(directory, "broken.json"));
            CheckLockedFile(Path.Combine(directory, "locked.json"));
            Console.WriteLine("PASS drawing translation cache recovery: timestamps, nulls, expiry, capacity, JSON and I/O");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static TranslationApiSettings Settings()
    {
        TranslationApiSettings settings = TranslationApiSettings.Default();
        settings.Model = "cache-test-model";
        settings.EnableCache = true;
        settings.CacheDays = 30;
        return settings;
    }

    private static void CheckDamagedEntries(string path)
    {
        TranslationApiSettings settings = Settings();
        long now = DateTime.UtcNow.Ticks;
        Dictionary<string, TranslationCache.CacheEntry> entries =
            new Dictionary<string, TranslationCache.CacheEntry>();
        entries[Key(settings, "valid")] = Entry(now, "Valid translation");
        entries[Key(settings, "expired")] = Entry(now - TimeSpan.FromDays(31).Ticks, "Expired");
        entries[Key(settings, "negative")] = Entry(-1, "Bad timestamp");
        entries[Key(settings, "too-large")] = Entry(Int64.MaxValue, "Bad timestamp");
        entries[Key(settings, "zero")] = Entry(0, "Bad timestamp");
        entries[Key(settings, "future")] = Entry(now + TimeSpan.FromDays(1).Ticks, "Future timestamp");
        entries[Key(settings, "null-record")] = null;
        entries[Key(settings, "empty")] = Entry(now, "   ");
        entries[Key(settings, "null-text")] = Entry(now, null);
        Write(path, entries);
        TranslationCache cache = new TranslationCache(path);
        string value;
        Require(cache.TryGet(settings, "valid", out value) && value == "Valid translation",
            "a damaged neighbour discarded the valid translation");
        foreach (string source in new[] { "negative", "too-large", "zero", "future",
            "null-record", "empty", "null-text", "expired" })
            Require(!cache.TryGet(settings, source, out value) && value == String.Empty,
                "damaged or expired entry was not a cache miss: " + source);
        cache.Put(settings, "recovered", "Recovered translation");
        TranslationCache reopened = new TranslationCache(path);
        Require(reopened.TryGet(settings, "recovered", out value) && value == "Recovered translation" &&
            reopened.TryGet(settings, "valid", out value), "writing did not recover or lost valid content");
        Require(Read(path).Count == 2, "rejected entries persisted after recovery write");
    }

    private static void CheckCapacity(string path)
    {
        TranslationApiSettings settings = Settings();
        long now = DateTime.UtcNow.Ticks;
        Dictionary<string, TranslationCache.CacheEntry> entries =
            new Dictionary<string, TranslationCache.CacheEntry>();
        for (int index = 0; index < 2005; index++)
            entries[Key(settings, "item-" + index)] = Entry(now - 2005 + index, "Translation " + index);
        entries[Key(settings, "null-record")] = null;
        Write(path, entries);
        TranslationCache cache = new TranslationCache(path);
        string value;
        Require(!cache.TryGet(settings, "item-0", out value) &&
            !cache.TryGet(settings, "item-4", out value) &&
            cache.TryGet(settings, "item-5", out value) &&
            cache.TryGet(settings, "item-2004", out value),
            "loading did not retain the newest 2000 valid entries");
        cache.Put(settings, "new", "Newest");
        Require(Read(path).Count == 2000 && !cache.TryGet(settings, "item-5", out value) &&
            cache.TryGet(settings, "new", out value), "write capacity or oldest-entry eviction failed");
    }

    private static void CheckBrokenJson(string path)
    {
        TranslationApiSettings settings = Settings();
        File.WriteAllText(path, "{broken");
        TranslationCache cache = new TranslationCache(path);
        string value;
        Require(!cache.TryGet(settings, "roof", out value), "corrupt JSON was not a miss");
        cache.Put(settings, "roof", "Roof");
        Require(new TranslationCache(path).TryGet(settings, "roof", out value) && value == "Roof",
            "corrupt JSON prevented persistent recovery");
    }

    private static void CheckLockedFile(string path)
    {
        TranslationApiSettings settings = Settings();
        File.WriteAllText(path, "{}");
        TranslationCache cache = new TranslationCache(path);
        using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            cache.Put(settings, "locked", "In-memory translation");
            string value;
            Require(cache.TryGet(settings, "locked", out value), "optional disk failure discarded memory cache");
            cache.Clear();
            Require(!cache.TryGet(settings, "locked", out value), "clear failed while cache file was locked");
        }
        cache.Put(settings, "unlocked", "Persistent translation");
        string restored;
        Require(new TranslationCache(path).TryGet(settings, "unlocked", out restored),
            "cache did not recover after the file lock was released");
    }

    private static string Key(TranslationApiSettings settings, string source)
    {
        string input = String.Join("\n", new[] { OpenAiCompatibleTranslator.PromptRevision,
            settings.Endpoint, settings.Model, settings.TargetLanguage, settings.Glossary, source });
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).
                Replace("-", "").ToLowerInvariant();
    }

    private static TranslationCache.CacheEntry Entry(long ticks, string value)
    {
        return new TranslationCache.CacheEntry { CreatedUtcTicks = ticks, Translation = value };
    }

    private static void Write(string path, Dictionary<string, TranslationCache.CacheEntry> entries)
    {
        File.WriteAllText(path, new JavaScriptSerializer().Serialize(entries), Encoding.UTF8);
    }

    private static Dictionary<string, TranslationCache.CacheEntry> Read(string path)
    {
        return new JavaScriptSerializer().Deserialize<Dictionary<string,
            TranslationCache.CacheEntry>>(File.ReadAllText(path, Encoding.UTF8));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
