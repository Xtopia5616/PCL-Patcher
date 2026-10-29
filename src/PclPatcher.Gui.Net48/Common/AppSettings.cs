using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace PclPatcher.Gui.Net48;

[DataContract]
internal sealed class SettingsPayload
{
    [DataMember(Name = "recentPclPaths")]
    public List<string> RecentPclPaths { get; set; }

    [DataMember(Name = "disclaimerVersion")]
    public string DisclaimerVersion { get; set; }

    [DataMember(Name = "disclaimerAcceptedAt")]
    public string DisclaimerAcceptedAt { get; set; }
}

/// <summary>
/// Per-user settings: the PCL paths the user already confirmed, plus which
/// disclaimer revision they acknowledged. Keeping them on disk means the second
/// launch of the patcher does not have to search the machine again, and the
/// first-run risk notice is only shown once per disclaimer revision.
/// </summary>
internal sealed class AppSettings
{
    private const int MaxRecent = 5;

    private readonly List<string> _recent = new List<string>();

    private string _disclaimerVersion;
    private string _disclaimerAcceptedAt;

    public IReadOnlyList<string> RecentPclPaths => _recent;

    /// <summary>Local time of the acknowledgement, for showing it back in the UI.</summary>
    public string DisclaimerAcceptedAt => _disclaimerAcceptedAt;

    public static string FilePath => IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PclPatcher", "settings.json");

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (!File.Exists(FilePath)) return settings;

            using var stream = File.OpenRead(FilePath);
            var serializer = new DataContractJsonSerializer(typeof(SettingsPayload));
            var payload = serializer.ReadObject(stream) as SettingsPayload;
            settings._disclaimerVersion = payload?.DisclaimerVersion;
            settings._disclaimerAcceptedAt = payload?.DisclaimerAcceptedAt;
            foreach (var path in payload?.RecentPclPaths ?? new List<string>())
            {
                var full = Normalize(path);
                if (full is null) continue;
                if (settings._recent.Contains(full, StringComparer.OrdinalIgnoreCase)) continue;
                settings._recent.Add(full);
                if (settings._recent.Count >= MaxRecent) break;
            }
        }
        catch
        {
            // A corrupt settings file must never stop the patcher from starting.
        }
        return settings;
    }

    /// <summary>
    /// Acceptance is tied to the exact notice revision: when the copy changes,
    /// the stored value no longer matches and the gate opens again.
    /// </summary>
    public bool HasAcceptedDisclaimer(string version) =>
        !string.IsNullOrEmpty(version) &&
        string.Equals(_disclaimerVersion, version, StringComparison.Ordinal);

    public void AcceptDisclaimer(string version)
    {
        if (string.IsNullOrEmpty(version)) return;
        _disclaimerVersion = version;
        _disclaimerAcceptedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Save();
    }

    public void Remember(string path)
    {
        var full = Normalize(path);
        if (full is null) return;

        _recent.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, full);
        while (_recent.Count > MaxRecent) _recent.RemoveAt(_recent.Count - 1);
        Save();
    }

    public void Save()
    {
        try
        {
            var directory = IOPath.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var payload = new SettingsPayload
            {
                RecentPclPaths = new List<string>(_recent),
                DisclaimerVersion = _disclaimerVersion,
                DisclaimerAcceptedAt = _disclaimerAcceptedAt
            };
            var temp = FilePath + ".tmp";
            using (var stream = File.Create(temp))
            {
                new DataContractJsonSerializer(typeof(SettingsPayload)).WriteObject(stream, payload);
            }
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(temp, FilePath);
        }
        catch
        {
            // Losing the recent list is harmless; never surface it as an error.
        }
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return IOPath.GetFullPath(path.Trim().Trim('"'));
        }
        catch
        {
            return null;
        }
    }
}
