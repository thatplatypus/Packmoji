using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;

namespace Packmoji.GitHub
{
    /// <summary>
    /// GitHub, as the place releases are. A release's file is downloaded by its own address, which
    /// needs no API and no token and is not counted against the API's limit. Only the list of a
    /// repository's releases comes from the API, and that is asked for by <c>pmj add</c> and
    /// <c>pmj update</c> alone.
    /// </summary>
    /// <remarks>
    /// A token is sent to the API and to nothing else, and is in no message this class makes: what
    /// GitHub says of a request is never repeated, only the status it answered with.
    /// </remarks>
    public sealed class GitHubReleaseHost : IReleaseHost
    {
        public const string Site = "https://github.com";
        public const string Api = "https://api.github.com";

        private const int PageSize = 100;

        // Five thousand releases. A repository with more is asking for something else.
        private const int MaxPages = 50;
        private const int MaxListBytes = 8 * 1024 * 1024;

        private readonly HttpClient _http;
        private readonly string _site;
        private readonly string _api;
        private readonly string? _token;
        private readonly string _userAgent;
        private readonly TimeSpan _quiet;

        /// <param name="site">Another address for <see cref="Site"/>, for a test of the native binary or a GitHub of one's own.</param>
        /// <param name="api">Another address for <see cref="Api"/>.</param>
        /// <param name="token">A token for the API, which raises its limit. Null for none.</param>
        /// <param name="userAgent">What pmj calls itself. GitHub's API refuses a request that does not say.</param>
        /// <param name="quiet">How long an answer may send nothing before it is given up. A minute, when it is not said.</param>
        public GitHubReleaseHost(HttpClient http, Uri? site = null, Uri? api = null, string? token = null, string userAgent = "pmj", TimeSpan? quiet = null)
        {
            ArgumentNullException.ThrowIfNull(http);
            _http = http;
            _site = (site?.AbsoluteUri ?? Site).TrimEnd('/');
            _api = (api?.AbsoluteUri ?? Api).TrimEnd('/');
            _token = string.IsNullOrWhiteSpace(token) ? null : token;
            _userAgent = userAgent;
            _quiet = quiet ?? TimeSpan.FromMinutes(1);
        }

        public async Task<ReadOnlyMemory<byte>?> DownloadAsync(RepositoryRef repository, string tag, string asset, int maxBytes, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(repository);
            var what = $"the release {tag} of {repository}";
            var address = $"{_site}/{repository.Owner}/{repository.Name}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(asset)}";
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, address), what, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw Unexpected(what, response.StatusCode);
            }

            if (response.Content.Headers.ContentLength > maxBytes || await ReadAsync(response, maxBytes, what, cancellationToken) is not { } bytes)
            {
                throw new PackageSourceException(new Diagnostic(
                    DiagnosticCodes.ArchiveInvalid,
                    $"The archive of {what} is too large.",
                    $"it is more than {maxBytes} bytes, which is the most an archive may be",
                    "tell its author: a package's archive holds its sources and nothing else"));
            }

            return bytes;
        }

        public async Task<IReadOnlyList<ReleaseInfo>> ListAsync(RepositoryRef repository, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(repository);
            var what = $"the releases of {repository}";
            var releases = new List<ReleaseInfo>();
            for (var page = 1; page <= MaxPages; page++)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_api}/repos/{repository.Owner}/{repository.Name}/releases?per_page={PageSize}&page={page}");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                if (_token is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                }

                using var response = await SendAsync(request, what, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return releases;
                }

                if (IsLimit(response))
                {
                    throw Limited(what, response);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new PackageSourceException(new Diagnostic(
                        DiagnosticCodes.GitHubUnreachable,
                        "GitHub refused the token it was given.",
                        $"the request for {what} was answered with status 401",
                        "check the token in GITHUB_TOKEN or GH_TOKEN, or unset it: a public repository's releases can be listed with none"));
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw Unexpected(what, response.StatusCode);
                }

                var body = await ReadAsync(response, MaxListBytes, what, cancellationToken) ?? throw NotUnderstood(what);
                var listed = Parse(body, what);
                releases.AddRange(listed.Published);
                if (listed.Count < PageSize)
                {
                    break;
                }
            }

            return releases;
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string what, CancellationToken cancellationToken)
        {
            request.Headers.UserAgent.ParseAdd(_userAgent);
            try
            {
                return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException failure)
            {
                throw Unreachable(what, failure.Message);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Unreachable(what, "there was no answer in time");
            }
            finally
            {
                request.Dispose();
            }
        }

        /// <summary>The body of an answer, or null when it is longer than <paramref name="maxBytes"/>.</summary>
        private async Task<byte[]?> ReadAsync(HttpResponseMessage response, int maxBytes, string what, CancellationToken cancellationToken)
        {
            // HttpClient's own limit on time ends when the start of an answer arrives. An answer that
            // then goes quiet would be waited on for ever, so each read is given a limit of its own.
            using var patience = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var read = new MemoryStream();
                var buffer = new byte[81_920];
                while (true)
                {
                    patience.CancelAfter(_quiet);
                    var count = await body.ReadAsync(buffer, patience.Token);
                    if (count == 0)
                    {
                        return read.ToArray();
                    }

                    if (read.Length + count > maxBytes)
                    {
                        return null;
                    }

                    read.Write(buffer, 0, count);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Unreachable(what, "the answer stopped coming");
            }
            catch (Exception failure) when (failure is HttpRequestException or IOException)
            {
                throw Unreachable(what, failure.Message);
            }
        }

        private static (int Count, List<ReleaseInfo> Published) Parse(byte[] body, string what)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw NotUnderstood(what);
                }

                var published = new List<ReleaseInfo>();
                foreach (var release in document.RootElement.EnumerateArray())
                {
                    if (release.ValueKind != JsonValueKind.Object
                        || !release.TryGetProperty("tag_name", out var tag)
                        || tag.ValueKind != JsonValueKind.String
                        || !release.TryGetProperty("assets", out var assets)
                        || assets.ValueKind != JsonValueKind.Array)
                    {
                        throw NotUnderstood(what);
                    }

                    var names = new List<string>();
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (asset.ValueKind != JsonValueKind.Object || !asset.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
                        {
                            throw NotUnderstood(what);
                        }

                        names.Add(name.GetString()!);
                    }

                    // A draft is not published. GitHub lists one only to those who may write to the repository.
                    if (!IsTrue(release, "draft"))
                    {
                        published.Add(new ReleaseInfo(tag.GetString()!, IsTrue(release, "prerelease"), names));
                    }
                }

                return (document.RootElement.GetArrayLength(), published);
            }
            catch (JsonException)
            {
                throw NotUnderstood(what);
            }
        }

        private static bool IsTrue(JsonElement release, string property) => release.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

        // GitHub answers 403 or 429 when the limit is reached, and says so in its headers.
        private static bool IsLimit(HttpResponseMessage response) =>
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
            && ((response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.FirstOrDefault() == "0") || response.Headers.Contains("retry-after"));

        private PackageSourceException Limited(string what, HttpResponseMessage response)
        {
            var lifts = response.Headers.TryGetValues("x-ratelimit-reset", out var reset)
                && long.TryParse(reset.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                && seconds < 253_402_300_800
                    ? $", which lifts at {DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC"
                    : "";
            return new PackageSourceException(new Diagnostic(
                DiagnosticCodes.GitHubRateLimited,
                $"GitHub would not give {what}.",
                $"its limit on requests has been reached{lifts}",
                _token is null
                    ? "set GITHUB_TOKEN to a token of yours, which raises the limit from 60 requests an hour to 5,000, or wait"
                    : "wait until the limit lifts, and try again"));
        }

        private static PackageSourceException Unreachable(string what, string why) =>
            new(new Diagnostic(
                DiagnosticCodes.GitHubUnreachable,
                "GitHub could not be reached.",
                $"the request for {what} failed: {why}",
                "check the network, and try again"));

        private static PackageSourceException Unexpected(string what, HttpStatusCode status) =>
            new(new Diagnostic(
                DiagnosticCodes.GitHubUnreachable,
                "GitHub answered in a way pmj did not expect.",
                $"the request for {what} was answered with status {(int)status}",
                "try again in a while: if it goes on, GitHub may be having trouble"));

        private static PackageSourceException NotUnderstood(string what) =>
            new(new Diagnostic(
                DiagnosticCodes.GitHubUnreachable,
                "GitHub answered in a way pmj did not expect.",
                $"the answer to the request for {what} is not a list of releases",
                "try again in a while: if it goes on, GitHub may be having trouble"));
    }
}
