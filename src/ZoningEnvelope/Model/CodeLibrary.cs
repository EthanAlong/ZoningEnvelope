using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ZoningEnvelope.Model
{
    /// <summary>
    /// Loads rule sets from the embedded codes/*.json (built-in) and from the user folder
    /// %APPDATA%\ZoningEnvelope\codes\*.json. A user file with the same id overrides the built-in one.
    /// The user folder is watched so a saved edit reloads without restarting Rhino.
    /// </summary>
    public class CodeLibrary : IDisposable
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public static string UserFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZoningEnvelope", "codes");

        private readonly List<ZoneCode> _codes = new List<ZoneCode>();
        private FileSystemWatcher _watcher;
        private DateTime _lastEvent = DateTime.MinValue;

        public IReadOnlyList<ZoneCode> Codes => _codes;
        public IEnumerable<ZoneCode> Zones => _codes.Where(c => c.Kind == "zone");
        public IEnumerable<ZoneCode> OpeningsTables => _codes.Where(c => c.Kind == "openings");
        public List<string> Errors { get; } = new List<string>();

        /// <summary>Raised (on a worker thread) when a file in the user folder changes.</summary>
        public event Action Changed;

        public ZoneCode Find(string id) => _codes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

        public void Reload()
        {
            _codes.Clear();
            Errors.Clear();

            var asm = Assembly.GetExecutingAssembly();
            foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith("codes.") && n.EndsWith(".json")))
            {
                try
                {
                    using (var s = asm.GetManifestResourceStream(res))
                    using (var r = new StreamReader(s))
                    {
                        var code = Parse(r.ReadToEnd(), res);
                        code.BuiltIn = true;
                        Add(code);
                    }
                }
                catch (Exception ex) { Errors.Add(res + ": " + ex.Message); }
            }

            try
            {
                Directory.CreateDirectory(UserFolder);
                foreach (var file in Directory.GetFiles(UserFolder, "*.json"))
                {
                    try
                    {
                        var code = Parse(File.ReadAllText(file), file);
                        code.FilePath = file;
                        Add(code);
                    }
                    catch (Exception ex) { Errors.Add(Path.GetFileName(file) + ": " + ex.Message); }
                }
            }
            catch (Exception ex) { Errors.Add("user folder: " + ex.Message); }

            _codes.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            EnsureWatcher();
        }

        /// <summary>Copy a built-in rule set to the user folder so it can be edited (does not overwrite).</summary>
        public string ExportBuiltIn(ZoneCode code)
        {
            Directory.CreateDirectory(UserFolder);
            string path = Path.Combine(UserFolder, code.Id + ".json");
            if (!File.Exists(path))
                File.WriteAllText(path, JsonSerializer.Serialize(code, JsonOptions));
            return path;
        }

        private static ZoneCode Parse(string json, string origin)
        {
            var code = JsonSerializer.Deserialize<ZoneCode>(json, JsonOptions);
            if (code == null || string.IsNullOrWhiteSpace(code.Id))
                throw new InvalidDataException("missing \"id\" in " + origin);
            if (string.IsNullOrWhiteSpace(code.Kind)) code.Kind = "zone";
            code.Kind = code.Kind.ToLowerInvariant();
            if (code.Kind == "zone")
            {
                code.Setbacks = code.Setbacks ?? new SetbackSet();
                code.Setbacks.Front = code.Setbacks.Front ?? new SetbackRule();
                code.Setbacks.Side = code.Setbacks.Side ?? new SetbackRule();
                code.Setbacks.Rear = code.Setbacks.Rear ?? new SetbackRule();
                code.Height = code.Height ?? new HeightRule { MaxFeet = 30 };
                if (code.Height.StoryHeightFeet <= 0) code.Height.StoryHeightFeet = 10;
                code.Stepbacks = code.Stepbacks ?? new List<StepbackRule>();
                code.Planes = code.Planes ?? new List<PlaneRule>();
                code.Notes = code.Notes ?? new List<string>();
            }
            return code;
        }

        private void Add(ZoneCode code)
        {
            _codes.RemoveAll(c => string.Equals(c.Id, code.Id, StringComparison.OrdinalIgnoreCase));
            _codes.Add(code);
        }

        private void EnsureWatcher()
        {
            if (_watcher != null) return;
            try
            {
                _watcher = new FileSystemWatcher(UserFolder, "*.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false,
                };
                FileSystemEventHandler h = (s, e) => Debounced();
                _watcher.Changed += h;
                _watcher.Created += h;
                _watcher.Deleted += h;
                _watcher.Renamed += (s, e) => Debounced();
                _watcher.EnableRaisingEvents = true;
            }
            catch { _watcher = null; }
        }

        private void Debounced()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastEvent).TotalMilliseconds < 300) return;
            _lastEvent = now;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }
}
