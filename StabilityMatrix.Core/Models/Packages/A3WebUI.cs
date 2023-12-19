﻿using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NLog;
using StabilityMatrix.Core.Attributes;
using StabilityMatrix.Core.Helper;
using StabilityMatrix.Core.Helper.Cache;
using StabilityMatrix.Core.Helper.HardwareInfo;
using StabilityMatrix.Core.Models.FileInterfaces;
using StabilityMatrix.Core.Models.Progress;
using StabilityMatrix.Core.Processes;
using StabilityMatrix.Core.Python;
using StabilityMatrix.Core.Services;

namespace StabilityMatrix.Core.Models.Packages;

[Singleton(typeof(BasePackage))]
public class A3WebUI(
    IGithubApiCache githubApi,
    ISettingsManager settingsManager,
    IDownloadService downloadService,
    IPrerequisiteHelper prerequisiteHelper
) : BaseGitPackage(githubApi, settingsManager, downloadService, prerequisiteHelper)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override string Name => "stable-diffusion-webui";
    public override string DisplayName { get; set; } = "Stable Diffusion WebUI";
    public override string Author => "AUTOMATIC1111";
    public override string LicenseType => "AGPL-3.0";
    public override string LicenseUrl =>
        "https://github.com/AUTOMATIC1111/stable-diffusion-webui/blob/master/LICENSE.txt";
    public override string Blurb => "A browser interface based on Gradio library for Stable Diffusion";
    public override string LaunchCommand => "launch.py";
    public override Uri PreviewImageUri =>
        new("https://github.com/AUTOMATIC1111/stable-diffusion-webui/raw/master/screenshot.png");
    public string RelativeArgsDefinitionScriptPath => "modules.cmd_args";

    public override PackageDifficulty InstallerSortOrder => PackageDifficulty.Recommended;

    public override SharedFolderMethod RecommendedSharedFolderMethod => SharedFolderMethod.Symlink;

    // From https://github.com/AUTOMATIC1111/stable-diffusion-webui/tree/master/models
    public override Dictionary<SharedFolderType, IReadOnlyList<string>> SharedFolders =>
        new()
        {
            [SharedFolderType.StableDiffusion] = new[] { "models/Stable-diffusion" },
            [SharedFolderType.ESRGAN] = new[] { "models/ESRGAN" },
            [SharedFolderType.RealESRGAN] = new[] { "models/RealESRGAN" },
            [SharedFolderType.SwinIR] = new[] { "models/SwinIR" },
            [SharedFolderType.Lora] = new[] { "models/Lora" },
            [SharedFolderType.LyCORIS] = new[] { "models/LyCORIS" },
            [SharedFolderType.ApproxVAE] = new[] { "models/VAE-approx" },
            [SharedFolderType.VAE] = new[] { "models/VAE" },
            [SharedFolderType.DeepDanbooru] = new[] { "models/deepbooru" },
            [SharedFolderType.Karlo] = new[] { "models/karlo" },
            [SharedFolderType.TextualInversion] = new[] { "embeddings" },
            [SharedFolderType.Hypernetwork] = new[] { "models/hypernetworks" },
            [SharedFolderType.ControlNet] = new[] { "models/controlnet/ControlNet" },
            [SharedFolderType.Codeformer] = new[] { "models/Codeformer" },
            [SharedFolderType.LDSR] = new[] { "models/LDSR" },
            [SharedFolderType.AfterDetailer] = new[] { "models/adetailer" },
            [SharedFolderType.T2IAdapter] = new[] { "models/controlnet/T2IAdapter" },
            [SharedFolderType.IpAdapter] = new[] { "models/controlnet/IpAdapter" },
            [SharedFolderType.InvokeIpAdapters15] = new[] { "models/controlnet/DiffusersIpAdapters" },
            [SharedFolderType.InvokeIpAdaptersXl] = new[] { "models/controlnet/DiffusersIpAdaptersXL" }
        };

    public override Dictionary<SharedOutputType, IReadOnlyList<string>>? SharedOutputFolders =>
        new()
        {
            [SharedOutputType.Extras] = new[] { "outputs/extras-images" },
            [SharedOutputType.Saved] = new[] { "log/images" },
            [SharedOutputType.Img2Img] = new[] { "outputs/img2img-images" },
            [SharedOutputType.Text2Img] = new[] { "outputs/txt2img-images" },
            [SharedOutputType.Img2ImgGrids] = new[] { "outputs/img2img-grids" },
            [SharedOutputType.Text2ImgGrids] = new[] { "outputs/txt2img-grids" }
        };

    [SuppressMessage("ReSharper", "ArrangeObjectCreationWhenTypeNotEvident")]
    public override List<LaunchOptionDefinition> LaunchOptions =>
        [
            new()
            {
                Name = "Host",
                Type = LaunchOptionType.String,
                DefaultValue = "localhost",
                Options = ["--server-name"]
            },
            new()
            {
                Name = "Port",
                Type = LaunchOptionType.String,
                DefaultValue = "7860",
                Options = ["--port"]
            },
            new()
            {
                Name = "VRAM",
                Type = LaunchOptionType.Bool,
                InitialValue = HardwareHelper.IterGpuInfo().Select(gpu => gpu.MemoryLevel).Max() switch
                {
                    MemoryLevel.Low => "--lowvram",
                    MemoryLevel.Medium => "--medvram",
                    _ => null
                },
                Options = ["--lowvram", "--medvram", "--medvram-sdxl"]
            },
            new()
            {
                Name = "Xformers",
                Type = LaunchOptionType.Bool,
                InitialValue = HardwareHelper.HasNvidiaGpu(),
                Options = ["--xformers"]
            },
            new()
            {
                Name = "API",
                Type = LaunchOptionType.Bool,
                InitialValue = true,
                Options = ["--api"]
            },
            new()
            {
                Name = "Auto Launch Web UI",
                Type = LaunchOptionType.Bool,
                InitialValue = false,
                Options = ["--autolaunch"]
            },
            new()
            {
                Name = "Skip Torch CUDA Check",
                Type = LaunchOptionType.Bool,
                InitialValue = !HardwareHelper.HasNvidiaGpu(),
                Options = ["--skip-torch-cuda-test"]
            },
            new()
            {
                Name = "Skip Python Version Check",
                Type = LaunchOptionType.Bool,
                InitialValue = true,
                Options = ["--skip-python-version-check"]
            },
            new()
            {
                Name = "No Half",
                Type = LaunchOptionType.Bool,
                Description = "Do not switch the model to 16-bit floats",
                InitialValue = HardwareHelper.PreferRocm() || HardwareHelper.PreferDirectML(),
                Options = ["--no-half"]
            },
            new()
            {
                Name = "Skip SD Model Download",
                Type = LaunchOptionType.Bool,
                InitialValue = false,
                Options = ["--no-download-sd-model"]
            },
            new()
            {
                Name = "Skip Install",
                Type = LaunchOptionType.Bool,
                Options = ["--skip-install"]
            },
            LaunchOptionDefinition.Extras
        ];

    public override IEnumerable<SharedFolderMethod> AvailableSharedFolderMethods =>
        new[] { SharedFolderMethod.Symlink, SharedFolderMethod.None };

    public override IEnumerable<TorchVersion> AvailableTorchVersions =>
        new[] { TorchVersion.Cpu, TorchVersion.Cuda, TorchVersion.Rocm };

    public override string MainBranch => "master";

    public override string OutputFolderName => "outputs";

    public override async Task InstallPackage(
        string installLocation,
        TorchVersion torchVersion,
        SharedFolderMethod selectedSharedFolderMethod,
        DownloadPackageVersionOptions versionOptions,
        IProgress<ProgressReport>? progress = null,
        Action<ProcessOutput>? onConsoleOutput = null
    )
    {
        progress?.Report(new ProgressReport(-1f, "Setting up venv", isIndeterminate: true));

        var venvRunner = await SetupVenv(installLocation, forceRecreate: true).ConfigureAwait(false);

        await venvRunner.PipInstall("--upgrade pip wheel", onConsoleOutput).ConfigureAwait(false);

        progress?.Report(new ProgressReport(-1f, "Installing requirements...", isIndeterminate: true));

        var requirements = new FilePath(installLocation, "requirements_versions.txt");

        var pipArgs = new PipInstallArgs()
            .WithTorch("==2.0.1")
            .WithTorchVision("==0.15.2")
            .WithTorchExtraIndex(
                torchVersion switch
                {
                    TorchVersion.Cpu => "cpu",
                    TorchVersion.Cuda => "cu118",
                    TorchVersion.Rocm => "rocm5.1.1",
                    _ => throw new ArgumentOutOfRangeException(nameof(torchVersion), torchVersion, null)
                }
            )
            .WithParsedFromRequirementsTxt(
                await requirements.ReadAllTextAsync().ConfigureAwait(false),
                excludePattern: "torch"
            );

        if (torchVersion == TorchVersion.Cuda)
        {
            pipArgs = pipArgs.WithXFormers("==0.0.20");
        }

        // v1.6.0 needs a httpx qualifier to fix a gradio issue
        if (versionOptions.VersionTag?.Contains("1.6.0") ?? false)
        {
            pipArgs = pipArgs.AddArg("httpx==0.24.1");
        }

        await venvRunner.PipInstall(pipArgs, onConsoleOutput).ConfigureAwait(false);

        progress?.Report(new ProgressReport(-1f, "Updating configuration", isIndeterminate: true));

        // Create and add {"show_progress_type": "TAESD"} to config.json
        // Only add if the file doesn't exist
        var configPath = Path.Combine(installLocation, "config.json");
        if (!File.Exists(configPath))
        {
            var config = new JsonObject { { "show_progress_type", "TAESD" } };
            await File.WriteAllTextAsync(configPath, config.ToString()).ConfigureAwait(false);
        }

        progress?.Report(new ProgressReport(1f, "Install complete", isIndeterminate: false));
    }

    public override async Task RunPackage(
        string installedPackagePath,
        string command,
        string arguments,
        Action<ProcessOutput>? onConsoleOutput
    )
    {
        await SetupVenv(installedPackagePath).ConfigureAwait(false);

        void HandleConsoleOutput(ProcessOutput s)
        {
            onConsoleOutput?.Invoke(s);

            if (!s.Text.Contains("Running on", StringComparison.OrdinalIgnoreCase))
                return;

            var regex = new Regex(@"(https?:\/\/)([^:\s]+):(\d+)");
            var match = regex.Match(s.Text);
            if (!match.Success)
                return;

            WebUrl = match.Value;
            OnStartupComplete(WebUrl);
        }

        var args = $"\"{Path.Combine(installedPackagePath, command)}\" {arguments}";

        VenvRunner.RunDetached(args.TrimEnd(), HandleConsoleOutput, OnExit);
    }
}
