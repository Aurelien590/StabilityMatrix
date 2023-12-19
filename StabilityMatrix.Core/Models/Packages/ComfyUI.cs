﻿using System.Diagnostics;
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
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace StabilityMatrix.Core.Models.Packages;

[Singleton(typeof(BasePackage))]
public class ComfyUI(
    IGithubApiCache githubApi,
    ISettingsManager settingsManager,
    IDownloadService downloadService,
    IPrerequisiteHelper prerequisiteHelper
) : BaseGitPackage(githubApi, settingsManager, downloadService, prerequisiteHelper)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public override string Name => "ComfyUI";
    public override string DisplayName { get; set; } = "ComfyUI";
    public override string Author => "comfyanonymous";
    public override string LicenseType => "GPL-3.0";
    public override string LicenseUrl => "https://github.com/comfyanonymous/ComfyUI/blob/master/LICENSE";
    public override string Blurb => "A powerful and modular stable diffusion GUI and backend";
    public override string LaunchCommand => "main.py";

    public override Uri PreviewImageUri =>
        new("https://github.com/comfyanonymous/ComfyUI/raw/master/comfyui_screenshot.png");
    public override bool ShouldIgnoreReleases => true;
    public override bool IsInferenceCompatible => true;
    public override string OutputFolderName => "output";
    public override PackageDifficulty InstallerSortOrder => PackageDifficulty.Advanced;

    public override SharedFolderMethod RecommendedSharedFolderMethod => SharedFolderMethod.Configuration;

    // https://github.com/comfyanonymous/ComfyUI/blob/master/folder_paths.py#L11
    public override Dictionary<SharedFolderType, IReadOnlyList<string>> SharedFolders =>
        new()
        {
            [SharedFolderType.StableDiffusion] = new[] { "models/checkpoints" },
            [SharedFolderType.Diffusers] = new[] { "models/diffusers" },
            [SharedFolderType.Lora] = new[] { "models/loras" },
            [SharedFolderType.CLIP] = new[] { "models/clip" },
            [SharedFolderType.InvokeClipVision] = new[] { "models/clip_vision" },
            [SharedFolderType.TextualInversion] = new[] { "models/embeddings" },
            [SharedFolderType.VAE] = new[] { "models/vae" },
            [SharedFolderType.ApproxVAE] = new[] { "models/vae_approx" },
            [SharedFolderType.ControlNet] = new[] { "models/controlnet/ControlNet" },
            [SharedFolderType.GLIGEN] = new[] { "models/gligen" },
            [SharedFolderType.ESRGAN] = new[] { "models/upscale_models" },
            [SharedFolderType.Hypernetwork] = new[] { "models/hypernetworks" },
            [SharedFolderType.IpAdapter] = new[] { "models/ipadapter/base" },
            [SharedFolderType.InvokeIpAdapters15] = new[] { "models/ipadapter/sd15" },
            [SharedFolderType.InvokeIpAdaptersXl] = new[] { "models/ipadapter/sdxl" },
            [SharedFolderType.T2IAdapter] = new[] { "models/controlnet/T2IAdapter" },
        };

    public override Dictionary<SharedOutputType, IReadOnlyList<string>>? SharedOutputFolders =>
        new() { [SharedOutputType.Text2Img] = new[] { "output" } };

    public override List<LaunchOptionDefinition> LaunchOptions =>
        [
            new LaunchOptionDefinition
            {
                Name = "Host",
                Type = LaunchOptionType.String,
                DefaultValue = "127.0.0.1",
                Options = ["--listen"]
            },
            new LaunchOptionDefinition
            {
                Name = "Port",
                Type = LaunchOptionType.String,
                DefaultValue = "8188",
                Options = ["--port"]
            },
            new LaunchOptionDefinition
            {
                Name = "VRAM",
                Type = LaunchOptionType.Bool,
                InitialValue = HardwareHelper.IterGpuInfo().Select(gpu => gpu.MemoryLevel).Max() switch
                {
                    MemoryLevel.Low => "--lowvram",
                    MemoryLevel.Medium => "--normalvram",
                    _ => null
                },
                Options = ["--highvram", "--normalvram", "--lowvram", "--novram"]
            },
            new LaunchOptionDefinition
            {
                Name = "Preview Method",
                Type = LaunchOptionType.Bool,
                InitialValue = "--preview-method auto",
                Options = ["--preview-method auto", "--preview-method latent2rgb", "--preview-method taesd"]
            },
            new LaunchOptionDefinition
            {
                Name = "Enable DirectML",
                Type = LaunchOptionType.Bool,
                InitialValue = HardwareHelper.PreferDirectML(),
                Options = ["--directml"]
            },
            new LaunchOptionDefinition
            {
                Name = "Use CPU only",
                Type = LaunchOptionType.Bool,
                InitialValue = !HardwareHelper.HasNvidiaGpu() && !HardwareHelper.HasAmdGpu(),
                Options = ["--cpu"]
            },
            new LaunchOptionDefinition
            {
                Name = "Disable Xformers",
                Type = LaunchOptionType.Bool,
                InitialValue = !HardwareHelper.HasNvidiaGpu(),
                Options = ["--disable-xformers"]
            },
            new LaunchOptionDefinition
            {
                Name = "Disable upcasting of attention",
                Type = LaunchOptionType.Bool,
                Options = ["--dont-upcast-attention"]
            },
            new LaunchOptionDefinition
            {
                Name = "Auto-Launch",
                Type = LaunchOptionType.Bool,
                Options = ["--auto-launch"]
            },
            LaunchOptionDefinition.Extras
        ];

    public override string MainBranch => "master";

    public override IEnumerable<TorchVersion> AvailableTorchVersions =>
        new[] { TorchVersion.Cpu, TorchVersion.Cuda, TorchVersion.DirectMl, TorchVersion.Rocm, TorchVersion.Mps };

    public override async Task InstallPackage(
        string installLocation,
        TorchVersion torchVersion,
        SharedFolderMethod selectedSharedFolderMethod,
        DownloadPackageVersionOptions versionOptions,
        IProgress<ProgressReport>? progress = null,
        Action<ProcessOutput>? onConsoleOutput = null
    )
    {
        progress?.Report(new ProgressReport(-1, "Setting up venv", isIndeterminate: true));
        // Setup venv
        await using var venvRunner = new PyVenvRunner(Path.Combine(installLocation, "venv"));
        venvRunner.WorkingDirectory = installLocation;
        await venvRunner.Setup(true, onConsoleOutput).ConfigureAwait(false);

        await venvRunner.PipInstall("--upgrade pip wheel", onConsoleOutput).ConfigureAwait(false);

        progress?.Report(new ProgressReport(-1f, "Installing Package Requirements...", isIndeterminate: true));

        var pipArgs = new PipInstallArgs();

        pipArgs = torchVersion switch
        {
            TorchVersion.DirectMl => pipArgs.WithTorchDirectML(),
            TorchVersion.Mps
                => pipArgs.AddArg("--pre").WithTorch().WithTorchVision().WithTorchExtraIndex("nightly/cpu"),
            _
                => pipArgs
                    .AddArg("--upgrade")
                    .WithTorch("~=2.1.0")
                    .WithTorchVision()
                    .WithTorchExtraIndex(
                        torchVersion switch
                        {
                            TorchVersion.Cpu => "cpu",
                            TorchVersion.Cuda => "cu121",
                            TorchVersion.Rocm => "rocm5.6",
                            _ => throw new ArgumentOutOfRangeException(nameof(torchVersion), torchVersion, null)
                        }
                    )
        };

        if (torchVersion == TorchVersion.Cuda)
        {
            pipArgs = pipArgs.WithXFormers("==0.0.22.post4");
        }

        var requirements = new FilePath(installLocation, "requirements.txt");

        pipArgs = pipArgs.WithParsedFromRequirementsTxt(
            await requirements.ReadAllTextAsync().ConfigureAwait(false),
            excludePattern: "torch"
        );

        await venvRunner.PipInstall(pipArgs, onConsoleOutput).ConfigureAwait(false);

        progress?.Report(new ProgressReport(1, "Installed Package Requirements", isIndeterminate: false));
    }

    public override async Task RunPackage(
        string installedPackagePath,
        string command,
        string arguments,
        Action<ProcessOutput>? onConsoleOutput
    )
    {
        await SetupVenv(installedPackagePath).ConfigureAwait(false);
        var args = $"\"{Path.Combine(installedPackagePath, command)}\" {arguments}";

        VenvRunner?.RunDetached(args.TrimEnd(), HandleConsoleOutput, HandleExit);
        return;

        void HandleExit(int i)
        {
            Debug.WriteLine($"Venv process exited with code {i}");
            OnExit(i);
        }

        void HandleConsoleOutput(ProcessOutput s)
        {
            onConsoleOutput?.Invoke(s);

            if (!s.Text.Contains("To see the GUI go to", StringComparison.OrdinalIgnoreCase))
                return;

            var regex = new Regex(@"(https?:\/\/)([^:\s]+):(\d+)");
            var match = regex.Match(s.Text);
            if (match.Success)
            {
                WebUrl = match.Value;
            }
            OnStartupComplete(WebUrl);
        }
    }

    public override Task SetupModelFolders(DirectoryPath installDirectory, SharedFolderMethod sharedFolderMethod) =>
        sharedFolderMethod switch
        {
            SharedFolderMethod.Symlink => base.SetupModelFolders(installDirectory, SharedFolderMethod.Symlink),
            SharedFolderMethod.Configuration => SetupModelFoldersConfig(installDirectory),
            SharedFolderMethod.None => Task.CompletedTask,
            _ => throw new ArgumentOutOfRangeException(nameof(sharedFolderMethod), sharedFolderMethod, null)
        };

    public override Task RemoveModelFolderLinks(DirectoryPath installDirectory, SharedFolderMethod sharedFolderMethod)
    {
        return sharedFolderMethod switch
        {
            SharedFolderMethod.Symlink => base.RemoveModelFolderLinks(installDirectory, sharedFolderMethod),
            SharedFolderMethod.Configuration => RemoveConfigSection(installDirectory),
            SharedFolderMethod.None => Task.CompletedTask,
            _ => throw new ArgumentOutOfRangeException(nameof(sharedFolderMethod), sharedFolderMethod, null)
        };
    }

    private async Task SetupModelFoldersConfig(DirectoryPath installDirectory)
    {
        var extraPathsYamlPath = installDirectory.JoinFile("extra_model_paths.yaml");
        var modelsDir = SettingsManager.ModelsDirectory;

        if (!extraPathsYamlPath.Exists)
        {
            Logger.Info("Creating extra_model_paths.yaml");
            extraPathsYamlPath.Create();
        }

        var yaml = await extraPathsYamlPath.ReadAllTextAsync().ConfigureAwait(false);
        using var sr = new StringReader(yaml);
        var yamlStream = new YamlStream();
        yamlStream.Load(sr);

        if (!yamlStream.Documents.Any())
        {
            yamlStream.Documents.Add(new YamlDocument(new YamlMappingNode()));
        }

        var root = yamlStream.Documents[0].RootNode;
        if (root is not YamlMappingNode mappingNode)
        {
            throw new Exception("Invalid extra_model_paths.yaml");
        }
        // check if we have a child called "stability_matrix"
        var stabilityMatrixNode = mappingNode.Children.FirstOrDefault(c => c.Key.ToString() == "stability_matrix");

        if (stabilityMatrixNode.Key != null)
        {
            if (stabilityMatrixNode.Value is not YamlMappingNode nodeValue)
                return;

            nodeValue.Children["checkpoints"] = Path.Combine(modelsDir, "StableDiffusion");
            nodeValue.Children["vae"] = Path.Combine(modelsDir, "VAE");
            nodeValue.Children["loras"] =
                $"{Path.Combine(modelsDir, "Lora")}\n" + $"{Path.Combine(modelsDir, "LyCORIS")}";
            nodeValue.Children["upscale_models"] =
                $"{Path.Combine(modelsDir, "ESRGAN")}\n"
                + $"{Path.Combine(modelsDir, "RealESRGAN")}\n"
                + $"{Path.Combine(modelsDir, "SwinIR")}";
            nodeValue.Children["embeddings"] = Path.Combine(modelsDir, "TextualInversion");
            nodeValue.Children["hypernetworks"] = Path.Combine(modelsDir, "Hypernetwork");
            nodeValue.Children["controlnet"] = string.Join(
                '\n',
                Path.Combine(modelsDir, "ControlNet"),
                Path.Combine(modelsDir, "T2IAdapter")
            );
            nodeValue.Children["clip"] = Path.Combine(modelsDir, "CLIP");
            nodeValue.Children["clip_vision"] = Path.Combine(modelsDir, "InvokeClipVision");
            nodeValue.Children["diffusers"] = Path.Combine(modelsDir, "Diffusers");
            nodeValue.Children["gligen"] = Path.Combine(modelsDir, "GLIGEN");
            nodeValue.Children["vae_approx"] = Path.Combine(modelsDir, "ApproxVAE");
            nodeValue.Children["ipadapter"] = string.Join(
                '\n',
                Path.Combine(modelsDir, "IpAdapter"),
                Path.Combine(modelsDir, "InvokeIpAdapters15"),
                Path.Combine(modelsDir, "InvokeIpAdaptersXl")
            );
        }
        else
        {
            stabilityMatrixNode = new KeyValuePair<YamlNode, YamlNode>(
                new YamlScalarNode("stability_matrix"),
                new YamlMappingNode
                {
                    { "checkpoints", Path.Combine(modelsDir, "StableDiffusion") },
                    { "vae", Path.Combine(modelsDir, "VAE") },
                    { "loras", $"{Path.Combine(modelsDir, "Lora")}\n{Path.Combine(modelsDir, "LyCORIS")}" },
                    {
                        "upscale_models",
                        $"{Path.Combine(modelsDir, "ESRGAN")}\n{Path.Combine(modelsDir, "RealESRGAN")}\n{Path.Combine(modelsDir, "SwinIR")}"
                    },
                    { "embeddings", Path.Combine(modelsDir, "TextualInversion") },
                    { "hypernetworks", Path.Combine(modelsDir, "Hypernetwork") },
                    {
                        "controlnet",
                        string.Join('\n', Path.Combine(modelsDir, "ControlNet"), Path.Combine(modelsDir, "T2IAdapter"))
                    },
                    { "clip", Path.Combine(modelsDir, "CLIP") },
                    { "clip_vision", Path.Combine(modelsDir, "InvokeClipVision") },
                    { "diffusers", Path.Combine(modelsDir, "Diffusers") },
                    { "gligen", Path.Combine(modelsDir, "GLIGEN") },
                    { "vae_approx", Path.Combine(modelsDir, "ApproxVAE") },
                    {
                        "ipadapter",
                        string.Join(
                            '\n',
                            Path.Combine(modelsDir, "IpAdapter"),
                            Path.Combine(modelsDir, "InvokeIpAdapters15"),
                            Path.Combine(modelsDir, "InvokeIpAdaptersXl")
                        )
                    }
                }
            );
        }

        var newRootNode = new YamlMappingNode();
        foreach (var child in mappingNode.Children.Where(c => c.Key.ToString() != "stability_matrix"))
        {
            newRootNode.Children.Add(child);
        }

        newRootNode.Children.Add(stabilityMatrixNode);

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithDefaultScalarStyle(ScalarStyle.Literal)
            .Build();

        var yamlData = serializer.Serialize(newRootNode);
        await extraPathsYamlPath.WriteAllTextAsync(yamlData).ConfigureAwait(false);
    }

    private static async Task RemoveConfigSection(DirectoryPath installDirectory)
    {
        var extraPathsYamlPath = installDirectory.JoinFile("extra_model_paths.yaml");

        if (!extraPathsYamlPath.Exists)
        {
            return;
        }

        var yaml = await extraPathsYamlPath.ReadAllTextAsync().ConfigureAwait(false);
        using var sr = new StringReader(yaml);
        var yamlStream = new YamlStream();
        yamlStream.Load(sr);

        if (!yamlStream.Documents.Any())
        {
            return;
        }

        var root = yamlStream.Documents[0].RootNode;
        if (root is not YamlMappingNode mappingNode)
        {
            return;
        }

        mappingNode.Children.Remove("stability_matrix");

        var serializer = new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        var yamlData = serializer.Serialize(mappingNode);

        await extraPathsYamlPath.WriteAllTextAsync(yamlData).ConfigureAwait(false);
    }
}
