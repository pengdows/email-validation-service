using System.Collections;

namespace EmailValidation.Core;

/// <summary>
/// A disposable-domain list fetched from a remote source and refreshed
/// periodically, implementing IReadOnlySet&lt;string&gt; so it can be assigned
/// directly to EmailValidatorOptions.AdditionalDisposableDomains - every check
/// sees whatever snapshot is current, no wiring changes needed elsewhere.
///
/// Defaults to groundcat/disposable-email-domain-list (MIT), which validates
/// entries by scanning MX records rather than just accumulating scraped domains
/// forever - see https://github.com/groundcat/disposable-email-domain-list.
///
/// A failed or empty fetch never clears the list - it keeps serving the last
/// known-good snapshot. A transient network blip should degrade to "using
/// slightly stale data," not "disposable detection silently stops working."
/// </summary>
public sealed class RemoteDisposableDomainList : IReadOnlySet<string>
{
    public static readonly Uri DefaultSourceUri =
        new("https://github.com/groundcat/disposable-email-domain-list/raw/master/domains.txt");

    private readonly HttpClient _httpClient;
    private readonly Uri _sourceUri;
    private volatile HashSet<string> _current = new(StringComparer.OrdinalIgnoreCase);

    public RemoteDisposableDomainList(HttpClient httpClient, Uri? sourceUri = null)
    {
        _httpClient = httpClient;
        _sourceUri = sourceUri ?? DefaultSourceUri;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var text = await _httpClient.GetStringAsync(_sourceUri, cancellationToken);

        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var domain = line.Trim();
            if (domain.Length > 0)
            {
                domains.Add(domain);
            }
        }

        // A "successful" fetch that comes back empty/truncated must not wipe
        // out an already-working list.
        if (domains.Count > 0)
        {
            _current = domains;
        }
    }

    public int Count => _current.Count;
    public bool Contains(string item) => _current.Contains(item);
    public bool IsProperSubsetOf(IEnumerable<string> other) => _current.IsProperSubsetOf(other);
    public bool IsProperSupersetOf(IEnumerable<string> other) => _current.IsProperSupersetOf(other);
    public bool IsSubsetOf(IEnumerable<string> other) => _current.IsSubsetOf(other);
    public bool IsSupersetOf(IEnumerable<string> other) => _current.IsSupersetOf(other);
    public bool Overlaps(IEnumerable<string> other) => _current.Overlaps(other);
    public bool SetEquals(IEnumerable<string> other) => _current.SetEquals(other);
    public IEnumerator<string> GetEnumerator() => _current.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
