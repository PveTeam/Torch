using System;
using System.Threading.Tasks;

namespace Torch.API.WebAPI;

public interface IUpdateQuery : IDisposable
{
    Task<UpdateRelease> GetLatestReleaseAsync(string repository, string branch = null);
}