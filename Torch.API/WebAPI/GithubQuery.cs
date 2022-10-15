using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using JorgeSerrano.Json;
using Version = SemanticVersioning.Version;

namespace Torch.API.WebAPI;

public class GithubQuery : IUpdateQuery
{
    private readonly HttpClient _client;

    public GithubQuery(string url)
    {
        if (url == null) throw new ArgumentNullException(nameof(url));
        
        _client = new()
        {
            BaseAddress = new(url),
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
            DefaultRequestHeaders =
            {
                {"User-Agent", "TorchAPI"}
            }
        };
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    public async Task<UpdateRelease> GetLatestReleaseAsync(string repository, string branch = null)
    {
        var response = await _client.GetFromJsonAsync<Release>($"/repos/{repository}/releases/latest", new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = new JsonSnakeCaseNamingPolicy()
        });

        if (response is null)
            throw new($"Unable to get latest release for {repository}");

        return new(Version.Parse(response.TagName), response.Assets.First(b => b.Name == "torch-server.zip").BrowserDownloadUrl);
    }
    
    private record Asset(
        string Url,
        int Id,
        string NodeId,
        string Name,
        string Label,
        Uploader Uploader,
        string ContentType,
        string State,
        int Size,
        int DownloadCount,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        string BrowserDownloadUrl
    );
    
    private record Author(
        string Login,
        int Id,
        string NodeId,
        string AvatarUrl,
        string GravatarId,
        string Url,
        string HtmlUrl,
        string FollowersUrl,
        string FollowingUrl,
        string GistsUrl,
        string StarredUrl,
        string SubscriptionsUrl,
        string OrganizationsUrl,
        string ReposUrl,
        string EventsUrl,
        string ReceivedEventsUrl,
        string Type,
        bool SiteAdmin
    );
    
    private record Release(
        string Url,
        string AssetsUrl,
        string UploadUrl,
        string HtmlUrl,
        int Id,
        Author Author,
        string NodeId,
        string TagName,
        string TargetCommitish,
        string Name,
        bool Draft,
        bool Prerelease,
        DateTime CreatedAt,
        DateTime PublishedAt,
        IReadOnlyList<Asset> Assets,
        string TarballUrl,
        string ZipballUrl,
        string Body
    );
    
    private record Uploader(
        string Login,
        int Id,
        string NodeId,
        string AvatarUrl,
        string GravatarId,
        string Url,
        string HtmlUrl,
        string FollowersUrl,
        string FollowingUrl,
        string GistsUrl,
        string StarredUrl,
        string SubscriptionsUrl,
        string OrganizationsUrl,
        string ReposUrl,
        string EventsUrl,
        string ReceivedEventsUrl,
        string Type,
        bool SiteAdmin
    );
}
