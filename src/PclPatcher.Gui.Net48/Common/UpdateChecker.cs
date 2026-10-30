#nullable enable

using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Xml.Linq;

namespace PclPatcher.Gui.Net48;

internal enum UpdateCheckState
{
    /// <summary>The build is the same as (or newer than) the latest release.</summary>
    UpToDate,

    /// <summary>GitHub has a newer release than this build.</summary>
    NewerVersion,

    /// <summary>This build carries no version metadata, so nothing can be compared.</summary>
    UnknownVersion,

    /// <summary>Every source failed; <see cref="UpdateCheckResult.Error"/> says why.</summary>
    Failed
}

/// <summary>Outcome of one “检查更新” click, rendered by the About page.</summary>
internal sealed class UpdateCheckResult
{
    public UpdateCheckState State { get; set; }

    /// <summary>Version of the running GUI, e.g. "1.1.2".</summary>
    public string? CurrentVersion { get; set; }

    /// <summary>Version of the newest release on GitHub, e.g. "1.1.2".</summary>
    public string? LatestVersion { get; set; }

    /// <summary>Release page to open in the browser.</summary>
    public string? ReleaseUrl { get; set; }

    public string? ReleaseName { get; set; }

    public string? PublishedAt { get; set; }

    /// <summary>Short, human readable reason when <see cref="State"/> is Failed.</summary>
    public string? Error { get; set; }
}

/// <summary>Release payload of the GitHub REST API (only the fields the UI shows).</summary>
[DataContract]
internal sealed class GitHubRelease
{
    [DataMember(Name = "tag_name")] public string? TagName { get; set; }
    [DataMember(Name = "name")] public string? Name { get; set; }
    [DataMember(Name = "html_url")] public string? HtmlUrl { get; set; }
    [DataMember(Name = "published_at")] public string? PublishedAt { get; set; }
    [DataMember(Name = "draft")] public bool Draft { get; set; }
    [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
}

/// <summary>
/// “Check for updates” against the project's GitHub releases. The GUI owns this
/// check (it is the component that knows its own version); nothing is sent but a
/// plain GET for the public release metadata, and a failure only means the About
/// page offers the release page instead of a version comparison.
///
/// Two sources are tried in order:
///   1. the REST API (full release notes and page URL);
///   2. the public Atom feed of the releases, which has no API rate limit.
/// </summary>
internal static class UpdateChecker
{
    public const string RepositoryUrl = "https://github.com/Xtopia5616/PCL-Patcher";
    public const string IssuesUrl = RepositoryUrl + "/issues";
    public const string ReleasesUrl = RepositoryUrl + "/releases";

    private const string ApiUrl = "https://api.github.com/repos/Xtopia5616/PCL-Patcher/releases/latest";
    private const string AtomUrl = ReleasesUrl + ".atom";

    /// <summary>
    /// Overrides the REST endpoint (mirror, proxy or a local feed while testing).
    /// Only the API URL is overridable; the Atom feed stays on github.com.
    /// </summary>
    private const string FeedVariable = "PCLPATCHER_UPDATE_API";

    private const int TimeoutMs = 10000;

    private static string? _currentVersion;

    /// <summary>
    /// Version of this build, taken from the assembly metadata emitted by
    /// <c>Directory.Build.props</c>, so a release bump touches one file only.
    /// Null when the metadata is missing: the About page then shows “版本未知”
    /// rather than guessing a number that would break the comparison.
    /// </summary>
    public static string? CurrentVersion => _currentVersion ??= ReadCurrentVersion();

    public static Task<UpdateCheckResult> CheckAsync()
    {
        var current = CurrentVersion;
        return Task.Run(() => Check(current));
    }

    /// <summary>
    /// Blocking check, kept separate from <see cref="CheckAsync"/> so the logic can
    /// be exercised without a window; call it from the UI only through the async
    /// wrapper.
    /// </summary>
    public static UpdateCheckResult Check(string? current)
    {
        if (string.IsNullOrEmpty(current))
        {
            return new UpdateCheckResult
            {
                State = UpdateCheckState.UnknownVersion,
                ReleaseUrl = ReleasesUrl
            };
        }

        var errors = new List<string>();
        GitHubRelease? release = null;
        try
        {
            release = FetchApi();
        }
        catch (Exception ex)
        {
            errors.Add(Describe(ex));
        }

        var latest = Normalize(release?.TagName);
        if (latest is null)
        {
            try
            {
                var feed = FetchAtom();
                if (feed is not null)
                {
                    // Keep the API's page URL/notes when only the tag was missing.
                    release = release is null ? feed : release;
                    latest = Normalize(feed.TagName);
                }
            }
            catch (Exception ex)
            {
                errors.Add(Describe(ex));
            }
        }

        if (latest is null)
        {
            // Both sources usually fail for the same reason (no network); one
            // line is enough, and the card appends its own full stop.
            var distinct = errors
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e!.Trim().TrimEnd('。', '.', ' '))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return new UpdateCheckResult
            {
                State = UpdateCheckState.Failed,
                CurrentVersion = current,
                Error = distinct.Count == 0 ? "GitHub 没有返回版本信息" : string.Join("；", distinct),
                ReleaseUrl = ReleasesUrl
            };
        }

        return new UpdateCheckResult
        {
            State = CompareVersions(current, latest) >= 0 ? UpdateCheckState.UpToDate : UpdateCheckState.NewerVersion,
            CurrentVersion = current,
            LatestVersion = latest,
            ReleaseUrl = release is not null && !string.IsNullOrEmpty(release.HtmlUrl) ? release.HtmlUrl : ReleasesUrl,
            ReleaseName = release?.Name,
            PublishedAt = release?.PublishedAt
        };
    }

    /// <summary>
    /// Compares two dotted versions ("1.1.2", "v1.1.2.0", "1.2.0-beta.1").
    /// A plain release sorts above a pre-release of the same number; missing
    /// segments count as zero.
    /// </summary>
    public static int CompareVersions(string? left, string? right)
    {
        Split(left, out var leftNumbers, out var leftPre);
        Split(right, out var rightNumbers, out var rightPre);

        var count = Math.Max(leftNumbers.Length, rightNumbers.Length);
        for (var i = 0; i < count; i++)
        {
            var result = ComparePart(
                i < leftNumbers.Length ? leftNumbers[i] : "0",
                i < rightNumbers.Length ? rightNumbers[i] : "0");
            if (result != 0) return result;
        }

        if (leftPre.Length == 0 && rightPre.Length > 0) return 1;
        if (rightPre.Length == 0 && leftPre.Length > 0) return -1;
        return string.Compare(leftPre, rightPre, StringComparison.OrdinalIgnoreCase);
    }

    private static void Split(string? version, out string[] numbers, out string pre)
    {
        var text = version?.Trim() ?? string.Empty;
        var plus = text.IndexOf('+');
        if (plus >= 0) text = text.Substring(0, plus);

        var dash = text.IndexOf('-');
        pre = string.Empty;
        if (dash >= 0)
        {
            pre = text.Substring(dash + 1);
            text = text.Substring(0, dash);
        }

        numbers = text.Split('.');
    }

    private static int ComparePart(string left, string right)
    {
        if (long.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) &&
            long.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            return a.CompareTo(b);

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadCurrentVersion()
    {
        try
        {
            var assembly = typeof(UpdateChecker).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = Normalize(informational);
            if (version is not null) return version;

            var name = assembly.GetName().Version;
            if (name is not null)
            {
                version = Normalize(name.ToString());
                if (version is not null) return version;
            }
        }
        catch
        {
            // Reflection failure means "unknown version", never a crash.
        }
        return null;
    }

    /// <summary>"v1.1.2+abc123" → "1.1.2"; null for empty or all-zero input.</summary>
    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var value = raw!.Trim();
        var plus = value.IndexOf('+');
        if (plus >= 0) value = value.Substring(0, plus);
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
        value = value.Trim();

        if (value.Length == 0) return null;
        if (value.All(c => c == '0' || c == '.')) return null;
        return value;
    }

    private static GitHubRelease FetchApi()
    {
        var endpoint = Environment.GetEnvironmentVariable(FeedVariable);
        if (string.IsNullOrWhiteSpace(endpoint)) endpoint = ApiUrl;

        var request = CreateRequest(endpoint.Trim());
        request.Accept = "application/vnd.github+json";
        using var response = (HttpWebResponse)request.GetResponse();
        using var stream = response.GetResponseStream()
                           ?? throw new WebException("GitHub 返回了空响应");

        var serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
        return serializer.ReadObject(stream) as GitHubRelease
               ?? throw new WebException("GitHub 返回的数据无法解析");
    }

    private static GitHubRelease? FetchAtom()
    {
        var request = CreateRequest(AtomUrl);
        using var response = (HttpWebResponse)request.GetResponse();
        using var stream = response.GetResponseStream();
        if (stream is null) return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var document = XDocument.Parse(reader.ReadToEnd());

        XNamespace ns = "http://www.w3.org/2005/Atom";
        var entry = document.Root?.Element(ns + "entry");
        if (entry is null) return null;

        var title = (string?)entry.Element(ns + "title");
        var link = entry.Elements(ns + "link")
            .FirstOrDefault(e => (string?)e.Attribute("rel") == "alternate");

        return new GitHubRelease
        {
            TagName = title?.Trim(),
            HtmlUrl = (string?)link?.Attribute("href")
        };
    }

    private static HttpWebRequest CreateRequest(string url)
    {
        // net48 on Windows 7 defaults to TLS 1.0; GitHub requires 1.2 or newer.
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        catch
        {
            // An already initialized service point is fine.
        }

        var uri = new Uri(url);
        var request = (HttpWebRequest)WebRequest.Create(uri);
        request.UserAgent = "PCL-Patcher/" + (CurrentVersion ?? "unknown") + " (+" + RepositoryUrl + ")";
        request.Timeout = TimeoutMs;
        request.ReadWriteTimeout = TimeoutMs;
        request.KeepAlive = false;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

        // A loopback feed (local mirror while testing) must never be sent through
        // the system proxy.
        if (uri.IsLoopback) request.Proxy = null;
        return request;
    }

    /// <summary>Turns transport failures into one short line the card can show.</summary>
    private static string Describe(Exception ex)
    {
        if (ex is AggregateException aggregate && aggregate.InnerException is not null)
            ex = aggregate.InnerException;

        switch (ex)
        {
            case WebException web:
                return DescribeWeb(web);
            case FormatException:
            case System.Xml.XmlException:
                return "GitHub 返回的数据格式不正确";
            default:
                var message = ex.Message ?? string.Empty;
                return message.Length > 120 ? message.Substring(0, 117) + "…" : message;
        }
    }

    /// <summary>
    /// A blocked or proxied network is the common failure here, so the raw
    /// localized socket text ("基础连接已经关闭…") is replaced by a hint plus the
    /// concrete status. HTTP errors still keep GitHub's status code.
    /// </summary>
    private static string DescribeWeb(WebException web)
    {
        if (web.Response is HttpWebResponse response)
        {
            switch ((int)response.StatusCode)
            {
                case 403:
                    return "GitHub 接口访问受限（403，可能是频率限制或网络封锁）";
                case 404:
                    return "GitHub 上还没有发布版本（404）";
                default:
                    return "GitHub 返回 HTTP " + (int)response.StatusCode;
            }
        }

        switch (web.Status)
        {
            case WebExceptionStatus.NameResolutionFailure:
                return "无法解析 GitHub 域名（DNS 失败）";
            case WebExceptionStatus.ConnectFailure:
                return "无法连接 GitHub（网络不可用或代理设置错误）";
            case WebExceptionStatus.Timeout:
                return "连接 GitHub 超时";
            case WebExceptionStatus.SecureChannelFailure:
            case WebExceptionStatus.TrustFailure:
                return "无法与 GitHub 建立加密连接（TLS/证书问题或网络受限）";
            case WebExceptionStatus.ReceiveFailure:
            case WebExceptionStatus.SendFailure:
            case WebExceptionStatus.ConnectionClosed:
                return "与 GitHub 的连接被中断（网络受限或代理拦截）";
            default:
                return "连接 GitHub 失败（" + web.Status + "）";
        }
    }
}
