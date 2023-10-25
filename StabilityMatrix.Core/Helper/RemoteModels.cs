﻿using StabilityMatrix.Core.Extensions;
using StabilityMatrix.Core.Models;

namespace StabilityMatrix.Core.Helper;

/// <summary>
/// Record of remote model resources
/// </summary>
public static class RemoteModels
{
    /// <summary>
    /// Root of huggingface upscalers
    /// </summary>
    private static Uri UpscalersRoot { get; } = new("https://huggingface.co/LykosAI/Upscalers/");

    /// <summary>
    /// Root of huggingface upscalers at the main branch
    /// </summary>
    private static Uri UpscalersRootMain { get; } = UpscalersRoot.Append("blob/main/");

    public static IReadOnlyList<RemoteResource> Upscalers { get; } =
        new RemoteResource[]
        {
            new()
            {
                Url = UpscalersRoot.Append(
                    "resolve/28a279ec60b5c47c9f39381dcaa997b9402ac09d/RealESRGAN/RealESRGAN_x2plus.pth"
                ),
                HashSha256 = "49fafd45f8fd7aa8d31ab2a22d14d91b536c34494a5cfe31eb5d89c2fa266abb",
                InfoUrl = new Uri("https://github.com/xinntao/ESRGAN"),
                Author = "xinntao",
                LicenseType = "Apache 2.0",
                LicenseUrl = new Uri(UpscalersRootMain, "RealESRGAN/LICENSE.txt"),
                ContextType = SharedFolderType.RealESRGAN
            },
            new()
            {
                Url = UpscalersRoot.Append(
                    "resolve/28a279ec60b5c47c9f39381dcaa997b9402ac09d/RealESRGAN/RealESRGAN_x2plus.pth"
                ),
                HashSha256 = "49fafd45f8fd7aa8d31ab2a22d14d91b536c34494a5cfe31eb5d89c2fa266abb",
                InfoUrl = new Uri("https://github.com/xinntao/ESRGAN"),
                Author = "xinntao",
                LicenseType = "Apache 2.0",
                LicenseUrl = new Uri(UpscalersRootMain, "RealESRGAN/LICENSE.txt"),
                ContextType = SharedFolderType.RealESRGAN
            },
            new()
            {
                Url = UpscalersRoot.Append(
                    "resolve/28a279ec60b5c47c9f39381dcaa997b9402ac09d/RealESRGAN/RealESRGAN_x4plus.pth"
                ),
                HashSha256 = "4fa0d38905f75ac06eb49a7951b426670021be3018265fd191d2125df9d682f1",
                InfoUrl = new Uri("https://github.com/xinntao/ESRGAN"),
                Author = "xinntao",
                LicenseType = "Apache 2.0",
                LicenseUrl = new Uri(UpscalersRootMain, "RealESRGAN/LICENSE.txt"),
                ContextType = SharedFolderType.RealESRGAN
            },
            new()
            {
                Url = UpscalersRoot.Append(
                    "resolve/28a279ec60b5c47c9f39381dcaa997b9402ac09d/RealESRGAN/RealESRGAN_x4plus_anime_6B.pth"
                ),
                HashSha256 = "f872d837d3c90ed2e05227bed711af5671a6fd1c9f7d7e91c911a61f155e99da",
                InfoUrl = new Uri("https://github.com/xinntao/ESRGAN"),
                Author = "xinntao",
                LicenseType = "Apache 2.0",
                LicenseUrl = new Uri(UpscalersRootMain, "RealESRGAN/LICENSE.txt"),
                ContextType = SharedFolderType.RealESRGAN
            },
            new()
            {
                Url = UpscalersRoot.Append(
                    "resolve/28a279ec60b5c47c9f39381dcaa997b9402ac09d/RealESRGAN/RealESRGAN_x4plus_anime_6B.pth"
                ),
                HashSha256 = "f872d837d3c90ed2e05227bed711af5671a6fd1c9f7d7e91c911a61f155e99da",
                InfoUrl = new Uri("https://github.com/xinntao/ESRGAN"),
                Author = "xinntao",
                LicenseType = "Apache 2.0",
                LicenseUrl = new Uri(UpscalersRootMain, "RealESRGAN/LICENSE.txt"),
                ContextType = SharedFolderType.RealESRGAN
            },
            new()
            {
                Url = UpscalersRoot.Append(
                    "resolve/4f813cc283de5cd66fe253bc05e66ca76bf68c51/SwinIR/SwinIR_4x.pth"
                ),
                HashSha256 = "99adfa91350a84c99e946c1eb3d8fce34bc28f57d807b09dc8fe40a316328c0a",
                InfoUrl = new Uri("https://github.com/JingyunLiang/SwinIR"),
                Author = "JingyunLiang",
                LicenseType = "Apache 2.0",
                LicenseUrl = new Uri(UpscalersRootMain, "SwinIR/LICENSE.txt"),
                ContextType = SharedFolderType.SwinIR
            },
            new()
            {
                Url = UpscalersRoot.Append(
                    "resolve/6a1d697dd18f8c4ff031f26e6dc523ada419517d/UltraMix/4x-UltraMix_Smooth.pth"
                ),
                HashSha256 = "7deeeac95ce7c28d616933b789f51642d169b200a6638edfb1c57ccecd903cd0",
                InfoUrl = new Uri("https://github.com/Kim2091"),
                Author = "Kim2091",
                LicenseType = "CC BY-NC-SA 4.0",
                LicenseUrl = new Uri(UpscalersRootMain, "UltraMix/LICENSE.txt"),
                ContextType = SharedFolderType.ESRGAN
            },
            new()
            {
                Url = UpscalersRoot.Append(
                    "resolve/6a1d697dd18f8c4ff031f26e6dc523ada419517d/UltraMix/4x-UltraMix_Restore.pth"
                ),
                HashSha256 = "e8982b435557baeea5a08066aede41a9b3c8a6512c8688dab6d326e91ba82fa3",
                InfoUrl = new Uri("https://github.com/Kim2091"),
                Author = "Kim2091",
                LicenseType = "CC BY-NC-SA 4.0",
                LicenseUrl = new Uri(UpscalersRootMain, "UltraMix/LICENSE.txt"),
                ContextType = SharedFolderType.ESRGAN
            },
            new()
            {
                Url = UpscalersRoot.Append(
                    "resolve/6a1d697dd18f8c4ff031f26e6dc523ada419517d/UltraMix/4x-UltraMix_Balanced.pth"
                ),
                HashSha256 = "e23ca000107aae95ec9b8d7d1bf150f7884f1361cd9d669bdf824d72529f0e26",
                InfoUrl = new Uri("https://github.com/Kim2091"),
                Author = "Kim2091",
                LicenseType = "CC BY-NC-SA 4.0",
                LicenseUrl = new Uri(UpscalersRootMain, "UltraMix/LICENSE.txt"),
                ContextType = SharedFolderType.ESRGAN
            }
        };
}
