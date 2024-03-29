﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NLog;
using Torch.API;

namespace Torch.Managers
{
    public partial class FilesystemManager : Manager
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        /// <summary>
        /// Temporary directory for Torch that is cleared every time the program is started.
        /// </summary>
        public string TempDirectory { get; }

        /// <summary>
        /// Directory that contains the current Torch assemblies.
        /// </summary>
        public string TorchDirectory { get; }

        public FilesystemManager(ITorchBase torchInstance) : base(torchInstance)
        {
            var torch = new FileInfo(typeof(FilesystemManager).Assembly.Location).Directory.FullName;
            TempDirectory = Directory.CreateDirectory(Path.Combine(torch, "tmp")).FullName;
            TorchDirectory = torch;

            _log.Debug($"Clearing tmp directory at {TempDirectory}");
            ClearTemp();
        }

        private void ClearTemp()
        {
            foreach (var file in Directory.GetFiles(TempDirectory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                }
                catch (UnauthorizedAccessException)
                {
                    _log.Debug($"Failed to delete file {file}, it's probably in use by another process'");
                }
                catch (Exception ex)
                {
                    _log.Warn($"Unhandled exception when clearing temp files. You may ignore this. {ex}");
                }
            }
        }

        public void SoftDelete(string path, string file)
        {
            var source = Path.Combine(path, file);
            if (!File.Exists(source))
                return;

            try
            {
                File.Delete(source);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                var tempFilePath = Path.Combine(path, file + ".old");
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);

                try
                {
                    File.Move(source, tempFilePath);
                }
                catch (UnauthorizedAccessException)
                {
                    // ignore
                }
            }
        }
    }
}
