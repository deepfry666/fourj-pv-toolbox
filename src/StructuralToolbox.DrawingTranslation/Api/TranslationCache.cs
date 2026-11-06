using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace StructuralToolbox.DrawingTranslation.Api
{
    internal sealed class TranslationProgress
    {
        internal TranslationProgress(int completed, int total, int requested,
            int cached, string phase)
        {
            Completed = completed;
            Total = total;
            Requested = requested;
            Cached = cached;
            Phase = phase ?? String.Empty;
        }

        internal int Completed { get; private set; }
        internal int Total { get; private set; }
        internal int Requested { get; private set; }
        internal int Cached { get; private set; }
        internal string Phase { get; private set; }
    }

    // 缓存只保存译文，不保存 API 密钥；文件位于当前用户配置目录，过期
    // 条目会在读写时清理，避免大图纸反复请求同一句工程文字。
    internal sealed class TranslationCache
    {
        private const int MaximumEntries = 2000;
        private readonly string _path;
        private readonly Dictionary<string, CacheEntry> _entries;

        internal TranslationCache(string path)
        {
            _path = path;
            _entries = LoadEntries(path);
            Trim();
        }

        internal static TranslationCache ForCurrentUser()
        {
            string directory = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
                "FourSeasonsPVToolbox");
            return new TranslationCache(Path.Combine(directory,
                "DrawingTranslationCache.json"));
        }

        internal bool TryGet(TranslationApiSettings settings, string source,
            out string translation)
        {
            translation = String.Empty;
            string key = Key(settings, source);
            CacheEntry entry;
            if (!_entries.TryGetValue(key, out entry))
                return false;
            long now = DateTime.UtcNow.Ticks;
            int days = settings.CacheDays <= 0 ? 30 : Math.Min(365, settings.CacheDays);
            if (!Usable(entry, now) ||
                now - entry.CreatedUtcTicks > TimeSpan.FromDays(days).Ticks)
            {
                _entries.Remove(key);
                return false;
            }
            translation = entry.Translation;
            return true;
        }

        internal void Put(TranslationApiSettings settings, string source,
            string translation)
        {
            if (String.IsNullOrWhiteSpace(source) ||
                String.IsNullOrWhiteSpace(translation))
                return;
            _entries[Key(settings, source)] = new CacheEntry {
                CreatedUtcTicks = DateTime.UtcNow.Ticks,
                Translation = translation
            };
            Trim();
            Save();
        }

        internal void Clear()
        {
            _entries.Clear();
            try
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private string Key(TranslationApiSettings settings, string source)
        {
            string value = String.Join("\n", new [] {
                OpenAiCompatibleTranslator.PromptRevision,
                settings.Endpoint ?? String.Empty,
                settings.Model ?? String.Empty,
                settings.TargetLanguage ?? String.Empty,
                settings.Glossary ?? String.Empty,
                source ?? String.Empty
            });
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder result = new StringBuilder(hash.Length * 2);
                foreach (byte part in hash)
                    result.Append(part.ToString("x2"));
                return result.ToString();
            }
        }

        private void Trim()
        {
            long now = DateTime.UtcNow.Ticks;
            List<string> damaged = new List<string>();
            foreach (KeyValuePair<string, CacheEntry> entry in _entries)
                if (!Usable(entry.Value, now)) damaged.Add(entry.Key);
            foreach (string key in damaged) _entries.Remove(key);
            if (_entries.Count <= MaximumEntries)
                return;
            List<KeyValuePair<string, CacheEntry>> ordered =
                new List<KeyValuePair<string, CacheEntry>>(_entries);
            ordered.Sort(delegate(KeyValuePair<string, CacheEntry> left,
                KeyValuePair<string, CacheEntry> right)
            {
                return left.Value.CreatedUtcTicks.CompareTo(
                    right.Value.CreatedUtcTicks);
            });
            int remove = _entries.Count - MaximumEntries;
            for (int index = 0; index < remove; index++)
                _entries.Remove(ordered[index].Key);
        }

        private static bool Usable(CacheEntry entry, long now)
        {
            // Validate file timestamps without constructing a DateTime: corrupt
            // ticks and future records must never interrupt network translation.
            return entry != null && !String.IsNullOrWhiteSpace(entry.Translation) &&
                entry.CreatedUtcTicks > 0 && entry.CreatedUtcTicks <= now;
        }

        private void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(_path);
                if (!String.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                string temporary = _path + ".tmp";
                File.WriteAllText(temporary,
                    new JavaScriptSerializer().Serialize(_entries),
                    Encoding.UTF8);
                if (File.Exists(_path))
                    File.Replace(temporary, _path, null);
                else
                    File.Move(temporary, _path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static Dictionary<string, CacheEntry> LoadEntries(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return new Dictionary<string, CacheEntry>();
                Dictionary<string, CacheEntry> entries =
                    new JavaScriptSerializer().Deserialize<Dictionary<string,
                        CacheEntry>>(File.ReadAllText(path, Encoding.UTF8));
                return entries ?? new Dictionary<string, CacheEntry>();
            }
            catch (Exception)
            {
                return new Dictionary<string, CacheEntry>();
            }
        }

        internal sealed class CacheEntry
        {
            public long CreatedUtcTicks { get; set; }
            public string Translation { get; set; }
        }
    }
}
