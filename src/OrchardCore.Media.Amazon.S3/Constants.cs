namespace OrchardCore.Media.Amazon.S3
{
    internal static class Constants
    {
        internal static class ValidationMessages
        {
            public const string BucketNameIsEmpty = "BucketName is required attribute for S3 Media";

            public const string RegionEndpointIsEmpty =
                "Region is required attribute for S3 Media, make sure it exists in Credentials section or ProfileName you specified";
        }
    }
}
