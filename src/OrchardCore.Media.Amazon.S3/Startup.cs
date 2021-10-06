using Amazon;
using Amazon.S3;
using OrchardCore.Settings;
using System.ComponentModel;

namespace OrchardCore.Media.Amazon.S3
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;

    using Modules;
    using Environment.Shell.Configuration;
    using Environment.Shell;
    using OrchardCore.FileStorage.Amazon.S3;
    using Core;
    using FileStorage;
    using OrchardCore.Media.Core.Events;
    using Events;

    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    [Feature("OrchardCore.Media.Amazon.S3")]
    public class Startup : Modules.StartupBase
    {
        private readonly ILogger _logger;
        private readonly IShellConfiguration _configuration;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public Startup(IShellConfiguration configuration,
            IWebHostEnvironment webHostEnvironment,
            ILogger<Startup> logger)
            => (_configuration, _webHostEnvironment, _logger)
                = (configuration, webHostEnvironment, logger);

        public override void ConfigureServices(IServiceCollection services)
        {
            var storeOptions = new AwsStorageOptions().BindConfiguration(_configuration);

            var validationErrors = storeOptions.Validate().ToList();
            var stringBuilder = new StringBuilder();

            if (validationErrors.Any())
            {
                foreach (var error in validationErrors)
                {
                    stringBuilder.Append(error.ErrorMessage);
                }

                if (_webHostEnvironment.IsDevelopment())
                {
                    _logger.LogInformation(
                        $"S3 Media configuration validation failed: {stringBuilder} fallback to File storage");
                }
                else
                {
                    _logger.LogError(
                        $"S3 Media configuration validation failed with errors: {stringBuilder} fallback to File storage");
                }
            }
            else
            {
                _logger.LogInformation(
                    $"Starting with S3 Media Configuration. { Environment.NewLine } BucketName: { storeOptions.BucketName }, { Environment.NewLine } BasePath: { storeOptions.BasePath }");

                services.AddSingleton<IMediaFileStoreCacheFileProvider>(serviceProvider =>
                {
                    var hostingEnvironment = serviceProvider.GetRequiredService<IWebHostEnvironment>();

                    if (string.IsNullOrWhiteSpace(hostingEnvironment.WebRootPath))
                    {
                        throw new Exception("The wwwroot folder for serving cache media files is missing.");
                    }

                    var mediaOptions = serviceProvider.GetRequiredService<IOptions<MediaOptions>>().Value;
                    var shellSettings = serviceProvider.GetRequiredService<ShellSettings>();
                    var logger = serviceProvider.GetRequiredService<ILogger<DefaultMediaFileStoreCacheFileProvider>>();

                    var mediaCachePath = GetMediaCachePath(hostingEnvironment,
                        DefaultMediaFileStoreCacheFileProvider.AssetsCachePath, shellSettings);

                    if (!Directory.Exists(mediaCachePath))
                    {
                        Directory.CreateDirectory(mediaCachePath);
                    }

                    return new DefaultMediaFileStoreCacheFileProvider(logger, mediaOptions.AssetsRequestPath,
                        mediaCachePath);
                });

                // Replace the default media file provider with the media cache file provider.
                services.Replace(ServiceDescriptor.Singleton<IMediaFileProvider>(serviceProvider =>
                    serviceProvider.GetRequiredService<IMediaFileStoreCacheFileProvider>()));

                // Register the media cache file provider as a file store cache provider.
                services.AddSingleton<IMediaFileStoreCache>(serviceProvider =>
                    serviceProvider.GetRequiredService<IMediaFileStoreCacheFileProvider>());

                services.AddSingleton<IAmazonS3>(serviceProvider =>
                {
                    var options = storeOptions;
                    if (options.Credentials == null)
                    {
                        return new AmazonS3Client();
                    }

                    var config = new AmazonS3Config
                    {
                        RegionEndpoint = RegionEndpoint.GetBySystemName(options.Credentials.RegionEndpoint),
                        UseHttp = true,
                        ForcePathStyle = true,
                        UseArnRegion = true
                    };

                    return new AmazonS3Client(options.Credentials.AccessKeyId,
                        options.Credentials.SecretKey,
                        config);
                });

                services.Replace(ServiceDescriptor.Singleton<IMediaFileStore>(serviceProvider =>
                {
                    var shellSettings = serviceProvider.GetRequiredService<ShellSettings>();
                    var mediaOptions = serviceProvider.GetRequiredService<IOptions<MediaOptions>>().Value;
                    var mediaEventHandlers = serviceProvider.GetServices<IMediaEventHandler>();
                    var mediaCreatingEventHandlers = serviceProvider.GetServices<IMediaCreatingEventHandler>();
                    var clock = serviceProvider.GetRequiredService<IClock>();
                    var logger = serviceProvider.GetRequiredService<ILogger<DefaultMediaFileStore>>();
                    var amazonS3Client = serviceProvider.GetService<IAmazonS3>();
                    var siteService = serviceProvider.GetService<ISiteService>();

                    var fileStore = new AwsFileStore(clock, storeOptions, amazonS3Client, siteService);

                    var siteName = siteService.GetSiteSettingsAsync().GetAwaiter().GetResult()
                        .SiteName;
                    var mediaUrlBase = $"/{siteName}";

                    return new DefaultMediaFileStore(fileStore,
                        mediaUrlBase,
                        mediaOptions.CdnBaseUrl,
                        mediaEventHandlers,
                        mediaCreatingEventHandlers,
                        logger);
                }));

                services.AddSingleton<IMediaEventHandler, DefaultMediaFileStoreCacheEventHandler>();
            }
        }

        private string GetMediaCachePath(IWebHostEnvironment hostingEnvironment,
            string assetsPath, ShellSettings shellSettings)
            => PathExtensions.Combine(hostingEnvironment.WebRootPath,
                assetsPath, shellSettings.Name);
    }
}
