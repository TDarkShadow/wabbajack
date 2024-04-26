using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.CommandLine.Invocation;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System;
using Wabbajack.CLI.Builder;
using Wabbajack.CLI.Verbs;
using Wabbajack.Common;
using Wabbajack.Downloaders.GameFile;
using Wabbajack.DTOs;
using Wabbajack.Hashing.xxHash64;
using Wabbajack.Paths.IO;
using Wabbajack.Paths;
using YamlDotNet.Core.Tokens;

namespace Wabbajack.CLI.Verbs;

public class HashGamefiles
{
    private readonly ILogger<HashGamefiles> _logger;
    private readonly GameLocator _locator;

    public HashGamefiles(ILogger<HashGamefiles> logger, GameLocator locator) {
        _logger = logger;
        _locator = locator;
    }

    public static VerbDefinition Definition = new VerbDefinition("hash-game-files",
        "Hashes a game's files for inclusion in the public github repo", new[]
        {
            new OptionDefinition(typeof(string), "g", "game", "WJ Game to index"),
            new OptionDefinition(typeof(AbsolutePath), "o", "output", "Output folder in which the file will be placed")
        });

    public async Task<int> Run(string game, AbsolutePath folder)
    {
        foreach (var availableGame in GameRegistry.Games.OrderBy(g => g.Value.HumanFriendlyGameName)) {
            if (_locator.IsInstalled(availableGame.Key) && availableGame.Value.HumanFriendlyGameName == game) {
                var location = _locator.GameLocation(availableGame.Key);
                var mainFile = availableGame.Value.MainExecutable!.Value.RelativeTo(location);

                if (!mainFile.FileExists())
                    _logger.LogWarning("Main file {file} for {game} does not exist", mainFile, availableGame.Key);

                var versionInfo = FileVersionInfo.GetVersionInfo(mainFile.ToString());

                _logger.LogInformation("[X] {Game} {Version} -> Path: {Path}", availableGame.Value.HumanFriendlyGameName, versionInfo.ProductVersion ?? versionInfo.FileVersion, location);

                var file = folder.Combine(availableGame.ToString(), versionInfo).WithExtension(new Extension(".json"));
                file.Parent.CreateDirectory();

                using var queue = new WorkQueue();
                var gameLocation = location;

                _logger.LogInformation("Hashing files for {Game} {Version} -> Path: {Path}", availableGame.Value.HumanFriendlyGameName, versionInfo.ProductVersion ?? versionInfo.FileVersion, location);

                var indexed = (await gameLocation
                    .EnumerateFiles()
                    .PMap(queue, async f => {
                        var hash = await f.FileHashCachedAsync();
                        if (hash == null) return null;
                        return new Archive(new GameFileSourceDownloader.State {
                            Game = availableGame,
                            GameFile = f.RelativeTo(gameLocation),
                            Hash = hash.Value,
                            GameVersion = version
                        }) {
                            Name = f.FileName.ToString(),
                            Hash = hash.Value,
                            Size = f.Size
                        };

                    })).NotNull().ToArray();

                _logger.LogInformation("Found and hashed {indexed.Length} files for {Game} {Version}", availableGame.Value.HumanFriendlyGameName, versionInfo.ProductVersion ?? versionInfo.FileVersion, location);
                await indexed.ToJsonAsync(file, prettyPrint: true);
            }
            else {
                _logger.LogInformation("Game {Game} is not found / installed.", availableGame.Value.HumanFriendlyGameName);
            }
        }
        return 0;
    }
}