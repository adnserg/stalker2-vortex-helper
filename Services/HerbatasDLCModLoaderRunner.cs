using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Stalker2ModManager.Models;

namespace Stalker2ModManager.Services
{
    public class HerbatasDLCModLoaderRunner
    {
        public RunResult Run(string baseGameDirectory, string? repakExecutablePath = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(baseGameDirectory))
                {
                    return RunResult.Fail("Game directory is empty.");
                }

                var requiredPath = Path.Combine(baseGameDirectory, "Stalker2", "Content", "GameLite", "DLCGameData");
                if (!Directory.Exists(requiredPath))
                {
                    return RunResult.Fail("Required folder 'Stalker2/Content/GameLite/DLCGameData' not found.");
                }

                var appBase = AppDomain.CurrentDomain.BaseDirectory;
                var tempFolderPath = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "HerbatasDLCModLoader");
                var repakFolderPath = Path.Combine(tempFolderPath, "HerbatasDLCModLoader");
                var modsFolderPath = Path.Combine(baseGameDirectory, "Stalker2", "Content", "Paks", "~mods");
                var configFolderPath = Path.Combine(repakFolderPath, "Stalker2", "Plugins", "PlatformProviderController", "Config", "Steam");

                Directory.CreateDirectory(configFolderPath);

                // Locate repak/repack executable: prefer game's Win64/bin, then provided path, then bundled
                string repakPathToUse;
                var gameBinDir = Path.Combine(baseGameDirectory, "Stalker2", "Binaries", "Win64", "bin");
                var repakInGame = Path.Combine(gameBinDir, "repak.exe");
                var repackInGame = Path.Combine(gameBinDir, "repack.exe");
                var defaultRepak = Path.Combine(appBase, "HerbatasDLCModLoader", "bin", "repak.exe");

                if (File.Exists(repakInGame))
                {
                    repakPathToUse = repakInGame;
                }
                else if (File.Exists(repackInGame))
                {
                    repakPathToUse = repackInGame;
                }
                else if (!string.IsNullOrWhiteSpace(repakExecutablePath))
                {
                    repakPathToUse = repakExecutablePath;
                }
                else
                {
                    repakPathToUse = defaultRepak;
                }
                if (!File.Exists(repakPathToUse))
                {
                    return RunResult.Fail("repak/repack executable not found. Place it in '.../Stalker2/Binaries/Win64/bin/', or at 'HerbatasDLCModLoader/bin/repak.exe', or specify a custom path.");
                }

                var headerContent = new[]
                {
                    "[OnlineSubsystem]",
                    "DefaultPlatformService=Steam",
                    string.Empty,
                    "[OnlineSubsystemSteam]",
                    "bEnabled=true",
                    "SteamDevAppId=1643320",
                    "Achievement_0_Id=Sandwich",
                    "Achievement_1_Id=PerfectBarter",
                    "Achievement_2_Id=PurchaseUpgrade",
                    "Achievement_3_Id=HeadshotStreak",
                    "Achievement_4_Id=RoyalFlush",
                    "Achievement_5_Id=MutantHunter",
                    "Achievement_6_Id=BreakEquipment",
                    "Achievement_7_Id=Gunsmith",
                    "Achievement_8_Id=ArtiHoarder",
                    "Achievement_9_Id=ArchiHoarder",
                    "Achievement_10_Id=BlueHoarder",
                    "Achievement_11_Id=Discovery",
                    "Achievement_12_Id=LonerShooter",
                    "Achievement_13_Id=UseDifferentWeapons",
                    "Achievement_14_Id=DrunkMaster",
                    "Achievement_15_Id=CatchingUp",
                    "Achievement_16_Id=MerryGoRound",
                    "Achievement_17_Id=WipedOut",
                    "Achievement_18_Id=CanOpener",
                    "Achievement_19_Id=Bouncy",
                    "Achievement_20_Id=ChimeraRun",
                    "Achievement_21_Id=SneakyClearLair",
                    "Achievement_22_Id=FinishSquad",
                    "Achievement_23_Id=Lockpick",
                    "Achievement_24_Id=HelloZone",
                    "Achievement_25_Id=GoodbyeLesserZone",
                    "Achievement_26_Id=MeetShram",
                    "Achievement_27_Id=MainPrize",
                    "Achievement_28_Id=CrystalLock",
                    "Achievement_29_Id=KillFaust",
                    "Achievement_30_Id=OSoznanie",
                    "Achievement_31_Id=DoctorMystery",
                    "Achievement_32_Id=MeetStrelok",
                    "Achievement_33_Id=MeetDegtyarev",
                    "Achievement_34_Id=EndingKorshunov",
                    "Achievement_35_Id=EndingShram",
                    "Achievement_36_Id=EndingStrelok",
                    "Achievement_37_Id=EndingKaymanov",
                    "Achievement_38_Id=SaveChernozem",
                    "Achievement_39_Id=TheSmartest",
                    "Achievement_40_Id=SaveSkadovsk",
                    "Achievement_41_Id=ShootApples",
                    "Achievement_42_Id=SaveZalesie",
                    "Achievement_43_Id=Povodok",
                    "Achievement_44_Id=VerySpecialWeapon",
                    "Achievement_45_Id=SitNearBonfire",
                    "Achievement_46_Id=KillStrelok",
                    "Achievement_47_Id=KillKorshunov",
                    "Achievement_48_Id=KillShram",
                    "Achievement_49_Id=AcceptFaustHelp",
                    "Achievement_50_Id=DeclineFaustHelp",
                    "Achievement_51_Id=BetweenTheLines",
                    "Achievement_52_Id=FindAllScanners",
                    string.Empty,
                    "[SteamDLCs]",
                    "PreOrder=1661623",
                    "Deluxe=1661620",
                    "Ultimate=1661621"
                };

                var subfolders = Directory
                    .GetDirectories(requiredPath)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .OrderBy(n => n)
                    .Select(n => $"{n}=480");

                var steamIniPath = Path.Combine(configFolderPath, "SteamPlatformProviderEngine.ini");
                File.WriteAllLines(steamIniPath, headerContent.Concat(subfolders));

                // Run repak: pack the repakFolderPath into temp root (repak writes HerbatasDLCModLoader.pak next to itself)
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = repakPathToUse,
                        Arguments = "pack --version V11 \"" + repakFolderPath + "\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrWhiteSpace(error))
                {
                    return RunResult.Fail("repak error: " + error.Trim());
                }

                var sourcePakPath = Path.Combine(tempFolderPath, "HerbatasDLCModLoader.pak");
                Directory.CreateDirectory(modsFolderPath);
                var destinationPakPath = Path.Combine(modsFolderPath, "HerbatasDLCModLoader.pak");

                if (!File.Exists(sourcePakPath))
                {
                    return RunResult.Fail("HerbatasDLCModLoader.pak not produced by repak.");
                }

                File.Copy(sourcePakPath, destinationPakPath, true);
                
                // Удаляем временную папку после успешного выполнения
                try
                {
                    if (Directory.Exists(tempFolderPath))
                    {
                        Directory.Delete(tempFolderPath, true);
                        Logger.Instance.LogInfo($"Temporary folder deleted successfully: {tempFolderPath}");
                    }
                }
                catch (Exception deleteEx)
                {
                    Logger.Instance.LogError("Error deleting temporary folder after successful run", deleteEx);
                }
                
                return RunResult.Ok(destinationPakPath, output);
            }
            catch (Exception ex)
            {
                // Пытаемся удалить временную папку даже при ошибке
                try
                {
                    var tempFolderPath = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "HerbatasDLCModLoader");
                    if (Directory.Exists(tempFolderPath))
                    {
                        Directory.Delete(tempFolderPath, true);
                        Logger.Instance.LogInfo($"Temporary folder deleted after error: {tempFolderPath}");
                    }
                }
                catch (Exception deleteEx)
                {
                    Logger.Instance.LogError("Error deleting temporary folder after exception", deleteEx);
                }
                
                Logger.Instance.LogError("Error in HerbatasDLCModLoaderRunner.Run", ex);
                return RunResult.Fail(ex.Message);
            }
        }

        public RunResult RunUsingConfig(ConfigService configService)
        {
            var config = configService.LoadPathsConfig();
            var baseDir = string.IsNullOrWhiteSpace(config.TargetPath) ? AppDomain.CurrentDomain.BaseDirectory : config.TargetPath;
            return Run(baseDir);
        }

        public readonly struct RunResult
        {
            public bool Success { get; }
            public string Message { get; }
            public string? Output { get; }

            private RunResult(bool success, string message, string? output)
            {
                Success = success;
                Message = message;
                Output = output;
            }

            public static RunResult Ok(string message, string? output) => new RunResult(true, message, output);
            public static RunResult Fail(string message) => new RunResult(false, message, null);
        }
    }
}


