using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Torch.API.Utils;
using Version = SemanticVersioning.Version;

namespace Torch.API.WebAPI.Update
{
    public class JenkinsQuery : IUpdateQuery
    {
        private const string ApiPath = "api/json";
        private readonly HttpClient _client;
        
        public JenkinsQuery(string url)
        {
            if (url == null) throw new ArgumentNullException(nameof(url));
            
            _client = new()
            {
                BaseAddress = new(url)
            };
        }

        public async Task<UpdateRelease> GetLatestReleaseAsync(string repository, string branch = null)
        {
            branch ??= "master";

            var response = await _client.GetFromJsonAsync<BranchResponse>($"/job/{repository}/job/{branch}/{ApiPath}");

            if (response is null)
                throw new($"Unable to get latest release for {repository}");

            var job = await _client.GetFromJsonAsync<Job>(
                $"/job/{repository}/job/{branch}/{response.LastBuild.Number}/{ApiPath}");
            
            if (job is null)
                throw new($"Unable to get latest release for job {repository}/{response.LastBuild.Number}");

            return new(job.Version, job.Url + job.Artifacts.First(b => b.FileName == "torch-server.zip").RelativePath);
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }

    public record BranchResponse(string Name, string Url, Build LastBuild, Build LastStableBuild);

    public record Build(int Number, string Url);

    public record Job(int Number, bool Building, string Description, string Result, string Url,
        [property: JsonConverter(typeof(SemanticVersionConverter))] Version Version,
                      IReadOnlyList<Artifact> Artifacts);
    
    public record Artifact(
        string DisplayPath,
        string FileName,
        string RelativePath
    );
}
