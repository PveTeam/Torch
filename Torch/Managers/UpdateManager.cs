using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Packaging;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Torch.API;
using Torch.API.WebAPI;

namespace Torch.Managers
{
    /// <summary>
    /// Handles updating of the DS and Torch plugins.
    /// </summary>
    public class UpdateManager : Manager
    {
        private readonly Timer _updatePollTimer;
        private readonly string _torchDir = ApplicationContext.Current.TorchDirectory.FullName;
        private readonly Logger _log = LogManager.GetCurrentClassLogger();

        [Dependency]
        private FilesystemManager _fsManager = null!;
        
        public UpdateManager(ITorchBase torchInstance) : base(torchInstance)
        {
            _updatePollTimer = null;
            //_updatePollTimer = new Timer(TimerElapsed, this, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        }

        /// <inheritdoc />
        public override void Attach()
        {
            CheckAndUpdateTorch();
        }

        private void TimerElapsed(object state)
        {
            CheckAndUpdateTorch();
        }
        
        private async void CheckAndUpdateTorch()
        {
            if (Torch.Config.NoUpdate || !Torch.Config.GetTorchUpdates)
                return;

            try
            {
                var updateSource = Torch.Config.UpdateSource;
                
                IUpdateQuery source = updateSource.SourceType switch
                {
                    UpdateSourceType.Github => new GithubQuery(updateSource.Url),
                    UpdateSourceType.Jenkins => new JenkinsQuery(updateSource.Url),
                    _ => throw new ArgumentOutOfRangeException()
                };
                
                var release = await source.GetLatestReleaseAsync(updateSource.Repository, updateSource.Branch);

                if (release.Version > Torch.TorchVersion)
                {
                    _log.Warn($"Updating Torch from version {Torch.TorchVersion} to version {release.Version}");
                    await UpdateAsync(release, _torchDir);
                    _log.Warn($"Torch version {release.Version} has been installed, please restart Torch to finish the process.");
                }
                else
                {
                    _log.Info("Torch is up to date.");
                }
            }
            catch (Exception e)
            {
                _log.Error("An error occured downloading the Torch update.");
                _log.Error(e);
            }
        }

        private async Task UpdateAsync(UpdateRelease release, string extractPath)
        {
            using var client = new HttpClient();
            await using var stream = await client.GetStreamAsync(release.ArtifactUrl);
            UpdateFromZip(stream, extractPath);
        }

        private void UpdateFromZip(Stream zipStream, string extractPath)
        {
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, true);
            
            foreach (var file in zip.Entries)
            {
                if(file.Name == "NLog-user.config" && File.Exists(Path.Combine(extractPath, file.FullName)))
                    continue;

                _log.Debug($"Unzipping {file.FullName}");
                var targetFile = Path.Combine(extractPath, file.FullName);
                _fsManager.SoftDelete(extractPath, file.FullName);
                file.ExtractToFile(targetFile, true);
            }

            //zip.ExtractToDirectory(extractPath); //throws exceptions sometimes?
        }

        /// <inheritdoc />
        public override void Detach()
        {
            _updatePollTimer?.Dispose();
        }
    }
}
