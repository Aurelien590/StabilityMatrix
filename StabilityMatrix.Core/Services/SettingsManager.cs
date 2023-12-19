﻿using System.ComponentModel;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AsyncAwaitBestPractices;
using NLog;
using StabilityMatrix.Core.Attributes;
using StabilityMatrix.Core.Helper;
using StabilityMatrix.Core.Models;
using StabilityMatrix.Core.Models.FileInterfaces;
using StabilityMatrix.Core.Models.Settings;
using StabilityMatrix.Core.Python;

namespace StabilityMatrix.Core.Services;

[Singleton(typeof(ISettingsManager))]
public class SettingsManager : ISettingsManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly ReaderWriterLockSlim FileLock = new();
    private bool isLoaded;

    private static string GlobalSettingsPath => Path.Combine(Compat.AppDataHome, "global.json");

    public string? LibraryDirOverride { private get; set; }

    private readonly string? originalEnvPath = Environment.GetEnvironmentVariable(
        "PATH",
        EnvironmentVariableTarget.Process
    );

    // Library properties
    public bool IsPortableMode { get; private set; }
    private string? libraryDir;
    public string LibraryDir
    {
        get
        {
            if (string.IsNullOrWhiteSpace(libraryDir))
            {
                throw new InvalidOperationException("LibraryDir is not set");
            }
            return libraryDir;
        }
        private set
        {
            var isChanged = libraryDir != value;

            libraryDir = value;

            // Only invoke if different
            if (isChanged)
            {
                LibraryDirChanged?.Invoke(this, value);
            }
        }
    }
    public bool IsLibraryDirSet => !string.IsNullOrWhiteSpace(libraryDir);

    // Dynamic paths from library
    public string DatabasePath => Path.Combine(LibraryDir, "StabilityMatrix.db");
    private string SettingsPath => Path.Combine(LibraryDir, "settings.json");
    public string ModelsDirectory => Path.Combine(LibraryDir, "Models");
    public string DownloadsDirectory => Path.Combine(LibraryDir, ".downloads");
    public List<string> PackageInstallsInProgress { get; set; } = new();

    public DirectoryPath TagsDirectory => new(LibraryDir, "Tags");

    public DirectoryPath ImagesDirectory => new(LibraryDir, "Images");
    public DirectoryPath ImagesInferenceDirectory => ImagesDirectory.JoinDir("Inference");
    public DirectoryPath ConsolidatedImagesDirectory => ImagesDirectory.JoinDir("Consolidated");

    public Settings Settings { get; private set; } = new();

    /// <inheritdoc />
    public event EventHandler<string>? LibraryDirChanged;

    /// <inheritdoc />
    public event EventHandler<RelayPropertyChangedEventArgs>? SettingsPropertyChanged;

    /// <inheritdoc />
    public event EventHandler? Loaded;

    /// <inheritdoc />
    public void RegisterOnLibraryDirSet(Action<string> handler)
    {
        if (IsLibraryDirSet)
        {
            handler(LibraryDir);
            return;
        }

        LibraryDirChanged += Handler;

        return;

        void Handler(object? sender, string dir)
        {
            LibraryDirChanged -= Handler;
            handler(dir);
        }
    }

    /// <inheritdoc />
    public SettingsTransaction BeginTransaction()
    {
        if (!IsLibraryDirSet)
        {
            throw new InvalidOperationException("LibraryDir not set when BeginTransaction was called");
        }
        return new SettingsTransaction(this, SaveSettingsAsync);
    }

    /// <inheritdoc />
    public void Transaction(Action<Settings> func, bool ignoreMissingLibraryDir = false)
    {
        if (!IsLibraryDirSet)
        {
            if (ignoreMissingLibraryDir)
            {
                func(Settings);
                return;
            }
            throw new InvalidOperationException("LibraryDir not set when Transaction was called");
        }
        using var transaction = BeginTransaction();
        func(transaction.Settings);
    }

    /// <inheritdoc />
    public void Transaction<TValue>(Expression<Func<Settings, TValue>> expression, TValue value)
    {
        if (expression.Body is not MemberExpression memberExpression)
        {
            throw new ArgumentException($"Expression must be a member expression, not {expression.Body.NodeType}");
        }

        var propertyInfo = memberExpression.Member as PropertyInfo;
        if (propertyInfo == null)
        {
            throw new ArgumentException(
                $"Expression member must be a property, not {memberExpression.Member.MemberType}"
            );
        }

        var name = propertyInfo.Name;

        // Set value
        using var transaction = BeginTransaction();
        propertyInfo.SetValue(transaction.Settings, value);

        // Invoke property changed event
        SettingsPropertyChanged?.Invoke(this, new RelayPropertyChangedEventArgs(name));
    }

    /// <inheritdoc />
    public void RelayPropertyFor<T, TValue>(
        T source,
        Expression<Func<T, TValue>> sourceProperty,
        Expression<Func<Settings, TValue>> settingsProperty,
        bool setInitial = false,
        TimeSpan? delay = null
    )
        where T : INotifyPropertyChanged
    {
        var sourceGetter = sourceProperty.Compile();
        var (propertyName, assigner) = Expressions.GetAssigner(sourceProperty);
        var sourceSetter = assigner.Compile();

        var settingsGetter = settingsProperty.Compile();
        var (targetPropertyName, settingsAssigner) = Expressions.GetAssigner(settingsProperty);
        var settingsSetter = settingsAssigner.Compile();

        var sourceTypeName = source.GetType().Name;

        // Update source when settings change
        SettingsPropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != targetPropertyName)
                return;

            // Skip if event is relay and the sender is the source, to prevent duplicate
            if (args.IsRelay && ReferenceEquals(sender, source))
                return;
            Logger.Trace(
                "[RelayPropertyFor] " + "Settings.{TargetProperty:l} -> {SourceType:l}.{SourceProperty:l}",
                targetPropertyName,
                sourceTypeName,
                propertyName
            );

            sourceSetter(source, settingsGetter(Settings));
        };

        // Set and Save settings when source changes
        source.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != propertyName)
                return;

            Logger.Trace(
                "[RelayPropertyFor] " + "{SourceType:l}.{SourceProperty:l} -> Settings.{TargetProperty:l}",
                sourceTypeName,
                propertyName,
                targetPropertyName
            );

            settingsSetter(Settings, sourceGetter(source));

            if (IsLibraryDirSet)
            {
                if (delay != null)
                {
                    SaveSettingsDelayed(delay.Value).SafeFireAndForget();
                }
                else
                {
                    SaveSettingsAsync().SafeFireAndForget();
                }
            }
            else
            {
                Logger.Warn("[RelayPropertyFor] LibraryDir not set when saving");
            }

            // Invoke property changed event, passing along sender
            SettingsPropertyChanged?.Invoke(sender, new RelayPropertyChangedEventArgs(targetPropertyName, true));
        };

        // Set initial value if requested
        if (setInitial)
        {
            sourceSetter(source, settingsGetter(Settings));
        }
    }

    /// <inheritdoc />
    public void RegisterPropertyChangedHandler<T>(
        Expression<Func<Settings, T>> settingsProperty,
        Action<T> onPropertyChanged
    )
    {
        var settingsGetter = settingsProperty.Compile();
        var (propertyName, _) = Expressions.GetAssigner(settingsProperty);

        // Invoke handler when settings change
        SettingsPropertyChanged += (_, args) =>
        {
            if (args.PropertyName != propertyName)
                return;

            onPropertyChanged(settingsGetter(Settings));
        };
    }

    /// <summary>
    /// Attempts to locate and set the library path
    /// Return true if found, false otherwise
    /// </summary>
    public bool TryFindLibrary(bool forceReload = false)
    {
        if (IsLibraryDirSet && !forceReload)
            return true;

        // 0. Check Override
        if (!string.IsNullOrEmpty(LibraryDirOverride))
        {
            var fullOverridePath = Path.GetFullPath(LibraryDirOverride);
            Logger.Info("Using library override path: {Path}", fullOverridePath);
            LibraryDir = fullOverridePath;
            SetStaticLibraryPaths();
            LoadSettings();
            return true;
        }

        // 1. Check portable mode
        var appDir = Compat.AppCurrentDir;
        IsPortableMode = File.Exists(Path.Combine(appDir, "Data", ".sm-portable"));
        if (IsPortableMode)
        {
            LibraryDir = appDir + "Data";
            SetStaticLibraryPaths();
            LoadSettings();
            return true;
        }

        // 2. Check %APPDATA%/StabilityMatrix/library.json
        FilePath libraryJsonFile = Compat.AppDataHome + "library.json";
        if (!libraryJsonFile.Exists)
            return false;

        try
        {
            var libraryJson = libraryJsonFile.ReadAllText();
            var librarySettings = JsonSerializer.Deserialize<LibrarySettings>(libraryJson);

            if (
                !string.IsNullOrWhiteSpace(librarySettings?.LibraryPath)
                && Directory.Exists(librarySettings.LibraryPath)
            )
            {
                LibraryDir = librarySettings.LibraryPath;
                SetStaticLibraryPaths();
                LoadSettings();
                return true;
            }
        }
        catch (Exception e)
        {
            Logger.Warn("Failed to read library.json in AppData: {Message}", e.Message);
        }
        return false;
    }

    // Set static classes requiring library path
    private void SetStaticLibraryPaths()
    {
        GlobalConfig.LibraryDir = LibraryDir;
        ArchiveHelper.HomeDir = LibraryDir;
        PyRunner.HomeDir = LibraryDir;
    }

    /// <summary>
    /// Save a new library path to %APPDATA%/StabilityMatrix/library.json
    /// </summary>
    public void SetLibraryPath(string path)
    {
        Compat.AppDataHome.Create();
        var libraryJsonFile = Compat.AppDataHome.JoinFile("library.json");

        var library = new LibrarySettings { LibraryPath = path };
        var libraryJson = JsonSerializer.Serialize(library, new JsonSerializerOptions { WriteIndented = true });
        libraryJsonFile.WriteAllText(libraryJson);

        // actually create the LibraryPath directory
        Directory.CreateDirectory(path);
    }

    /// <summary>
    /// Enable and create settings files for portable mode
    /// Creates the ./Data directory and the `.sm-portable` marker file
    /// </summary>
    public void SetPortableMode()
    {
        // Get app directory
        var appDir = Compat.AppCurrentDir;
        // Create data directory
        var dataDir = appDir.JoinDir("Data");
        dataDir.Create();
        // Create marker file
        dataDir.JoinFile(".sm-portable").Create();
    }

    /// <summary>
    /// Iterable of installed packages using the old absolute path format.
    /// Can be called with Any() to check if the user needs to migrate.
    /// </summary>
    public IEnumerable<InstalledPackage> GetOldInstalledPackages()
    {
        var oldSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StabilityMatrix",
            "settings.json"
        );

        if (!File.Exists(oldSettingsPath))
            yield break;

        var oldSettingsJson = File.ReadAllText(oldSettingsPath);
        var oldSettings = JsonSerializer.Deserialize<Settings>(
            oldSettingsJson,
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }
        );

        // Absolute paths are old formats requiring migration
#pragma warning disable CS0618
        var oldPackages = oldSettings?.InstalledPackages.Where(package => package.Path != null);
#pragma warning restore CS0618

        if (oldPackages == null)
            yield break;

        foreach (var package in oldPackages)
        {
            yield return package;
        }
    }

    public Guid GetOldActivePackageId()
    {
        var oldSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StabilityMatrix",
            "settings.json"
        );

        if (!File.Exists(oldSettingsPath))
            return default;

        var oldSettingsJson = File.ReadAllText(oldSettingsPath);
        var oldSettings = JsonSerializer.Deserialize<Settings>(
            oldSettingsJson,
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }
        );

        if (oldSettings == null)
            return default;

        return oldSettings.ActiveInstalledPackageId ?? default;
    }

    public void AddPathExtension(string pathExtension)
    {
        Settings.PathExtensions ??= new List<string>();
        Settings.PathExtensions.Add(pathExtension);
        SaveSettings();
    }

    public string GetPathExtensionsAsString()
    {
        return string.Join(";", Settings.PathExtensions ?? new List<string>());
    }

    /// <summary>
    /// Insert path extensions to the front of the PATH environment variable
    /// </summary>
    public void InsertPathExtensions()
    {
        if (Settings.PathExtensions == null)
            return;
        var toInsert = GetPathExtensionsAsString();
        // Append the original path, if any
        if (originalEnvPath != null)
        {
            toInsert += $";{originalEnvPath}";
        }
        Environment.SetEnvironmentVariable("PATH", toInsert, EnvironmentVariableTarget.Process);
    }

    public void UpdatePackageVersionNumber(Guid id, InstalledPackageVersion? newVersion)
    {
        var package = Settings.InstalledPackages.FirstOrDefault(x => x.Id == id);
        if (package == null || newVersion == null)
        {
            return;
        }

        package.Version = newVersion;
        SaveSettings();
    }

    public void SetLastUpdateCheck(InstalledPackage package)
    {
        var installedPackage = Settings.InstalledPackages.First(p => p.DisplayName == package.DisplayName);
        installedPackage.LastUpdateCheck = package.LastUpdateCheck;
        installedPackage.UpdateAvailable = package.UpdateAvailable;
        SaveSettings();
    }

    public List<LaunchOption> GetLaunchArgs(Guid packageId)
    {
        var packageData = Settings.InstalledPackages.FirstOrDefault(x => x.Id == packageId);
        return packageData?.LaunchArgs ?? new();
    }

    public void SaveLaunchArgs(Guid packageId, List<LaunchOption> launchArgs)
    {
        var packageData = Settings.InstalledPackages.FirstOrDefault(x => x.Id == packageId);
        if (packageData == null)
        {
            return;
        }
        // Only save if not null or default
        var toSave = launchArgs.Where(opt => !opt.IsEmptyOrDefault()).ToList();

        packageData.LaunchArgs = toSave;
        SaveSettings();
    }

    public string? GetActivePackageHost()
    {
        var package = Settings.InstalledPackages.FirstOrDefault(x => x.Id == Settings.ActiveInstalledPackageId);
        if (package == null)
            return null;
        var hostOption = package.LaunchArgs?.FirstOrDefault(x => x.Name.ToLowerInvariant() == "host");
        if (hostOption?.OptionValue != null)
        {
            return hostOption.OptionValue as string;
        }
        return hostOption?.DefaultValue as string;
    }

    public string? GetActivePackagePort()
    {
        var package = Settings.InstalledPackages.FirstOrDefault(x => x.Id == Settings.ActiveInstalledPackageId);
        if (package == null)
            return null;
        var portOption = package.LaunchArgs?.FirstOrDefault(x => x.Name.ToLowerInvariant() == "port");
        if (portOption?.OptionValue != null)
        {
            return portOption.OptionValue as string;
        }
        return portOption?.DefaultValue as string;
    }

    public void SetSharedFolderCategoryVisible(SharedFolderType type, bool visible)
    {
        Settings.SharedFolderVisibleCategories ??= new SharedFolderType();
        if (visible)
        {
            Settings.SharedFolderVisibleCategories |= type;
        }
        else
        {
            Settings.SharedFolderVisibleCategories &= ~type;
        }
        SaveSettings();
    }

    public bool IsSharedFolderCategoryVisible(SharedFolderType type)
    {
        // False for default
        if (type == 0)
            return false;
        return Settings.SharedFolderVisibleCategories?.HasFlag(type) ?? false;
    }

    public bool IsEulaAccepted()
    {
        if (!File.Exists(GlobalSettingsPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GlobalSettingsPath)!);
            File.Create(GlobalSettingsPath).Close();
            File.WriteAllText(GlobalSettingsPath, "{}");
            return false;
        }

        var json = File.ReadAllText(GlobalSettingsPath);
        var globalSettings = JsonSerializer.Deserialize<GlobalSettings>(json);
        return globalSettings?.EulaAccepted ?? false;
    }

    public void SetEulaAccepted()
    {
        var globalSettings = new GlobalSettings { EulaAccepted = true };
        var json = JsonSerializer.Serialize(globalSettings);
        File.WriteAllText(GlobalSettingsPath, json);
    }

    public void IndexCheckpoints()
    {
        Settings.InstalledModelHashes ??= new HashSet<string>();
        if (Settings.InstalledModelHashes.Any())
            return;

        var sw = new Stopwatch();
        sw.Start();

        var modelHashes = new HashSet<string>();
        var sharedModelDirectory = Path.Combine(LibraryDir, "Models");

        if (!Directory.Exists(sharedModelDirectory))
            return;

        var connectedModelJsons = Directory.GetFiles(
            sharedModelDirectory,
            "*.cm-info.json",
            SearchOption.AllDirectories
        );
        foreach (var jsonFile in connectedModelJsons)
        {
            var json = File.ReadAllText(jsonFile);
            var connectedModel = JsonSerializer.Deserialize<ConnectedModelInfo>(json);

            if (connectedModel?.Hashes.BLAKE3 != null)
            {
                modelHashes.Add(connectedModel.Hashes.BLAKE3);
            }
        }

        Transaction(s => s.InstalledModelHashes = modelHashes);

        sw.Stop();
        Logger.Info($"Indexed {modelHashes.Count} checkpoints in {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Loads settings from the settings file
    /// If the settings file does not exist, it will be created with default values
    /// </summary>
    protected virtual void LoadSettings()
    {
        FileLock.EnterReadLock();
        try
        {
            var settingsFile = new FilePath(SettingsPath);

            if (!settingsFile.Exists)
            {
                settingsFile.Directory?.Create();
                settingsFile.Create();

                var settingsJson = JsonSerializer.Serialize(Settings);
                settingsFile.WriteAllText(settingsJson);

                Loaded?.Invoke(this, EventArgs.Empty);
                isLoaded = true;
                return;
            }

            using var fileStream = settingsFile.Info.OpenRead();

            if (fileStream.Length == 0)
            {
                Logger.Warn("Settings file is empty, using default settings");
                return;
            }

            if (
                JsonSerializer.Deserialize(fileStream, SettingsSerializerContext.Default.Settings) is { } loadedSettings
            )
            {
                Settings = loadedSettings;
                isLoaded = true;
            }

            Loaded?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            FileLock.ExitReadLock();
        }
    }

    protected virtual void SaveSettings()
    {
        FileLock.TryEnterWriteLock(TimeSpan.FromSeconds(30));
        try
        {
            var settingsFile = new FilePath(SettingsPath);

            if (!settingsFile.Exists)
            {
                settingsFile.Directory?.Create();
                settingsFile.Create();
            }

            if (!isLoaded)
                return;

            if (SystemInfo.GetDiskFreeSpaceBytes(SettingsPath) is < 1 * SystemInfo.Mebibyte)
            {
                Logger.Warn("Not enough disk space to save settings");
                return;
            }

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(Settings, SettingsSerializerContext.Default.Settings);

            File.WriteAllBytes(SettingsPath, jsonBytes);
        }
        finally
        {
            FileLock.ExitWriteLock();
        }
    }

    private Task SaveSettingsAsync()
    {
        return Task.Run(SaveSettings);
    }

    private CancellationTokenSource? delayedSaveCts;

    private Task SaveSettingsDelayed(TimeSpan delay)
    {
        var cts = new CancellationTokenSource();

        var oldCancellationToken = Interlocked.Exchange(ref delayedSaveCts, cts);

        try
        {
            oldCancellationToken?.Cancel();
        }
        catch (ObjectDisposedException) { }

        return Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(delay, cts.Token).ConfigureAwait(false);

                    await SaveSettingsAsync().ConfigureAwait(false);
                }
                catch (TaskCanceledException) { }
                finally
                {
                    cts.Dispose();
                }
            },
            CancellationToken.None
        );
    }
}
