using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "Amazon S3 Media Storage",
    Author = "ngv",
    Website = "https://github.com/neglectedvalue",
    Version = ManifestConstants.OrchardCoreVersion,
    Category = "Media"
)]

[assembly: Feature(
    Id = "OrchardCore.Media.Amazon.S3",
    Name = "Amazon S3 Media Storage",
    Description = "Provides ability to store Media data in the AWS cloud.",
    Dependencies = new[]
    {
        "OrchardCore.Media.Cache"
    },
    Category = "Media"
)]