using Amazon;
using Amazon.Runtime.CredentialManagement;

namespace OrchardCore.Media.Amazon.S3
{
    using System;
    using Microsoft.Extensions.Configuration;
    using Environment.Shell.Configuration;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using OrchardCore.FileStorage.Amazon.S3;
    
    public static class AwsStorageOptionsExtension
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        public static IEnumerable<ValidationResult> Validate(this AwsStorageOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.BucketName))
            {
                yield return new ValidationResult(Constants.ValidationMessages.BucketNameIsEmpty);
            }

            if (string.IsNullOrWhiteSpace(options.Credentials?.RegionEndpoint))
            {
                yield return new ValidationResult(Constants.ValidationMessages.RegionEndpointIsEmpty);
            }
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="options"></param>
        /// <param name="shellConfiguration"></param>
        /// <returns></returns>
        public static AwsStorageOptions BindConfiguration(this AwsStorageOptions options, IShellConfiguration shellConfiguration)
        {
            var section = shellConfiguration.GetSection("OrchardCore_Media_Amazon_S3");

            if (section == null)
            {
                return options;
            }

            options.BucketName = section.GetValue(nameof(options.BucketName), String.Empty);
            options.BasePath = section.GetValue(nameof(options.BasePath), String.Empty);

            var credentials = section.GetSection("Credentials");
            if (credentials.Exists())
            {
                options.Credentials = new AwsStorageCredentials
                {
                    RegionEndpoint =
                        credentials.GetValue(nameof(options.Credentials.RegionEndpoint), RegionEndpoint.USEast1.SystemName),
                    SecretKey = credentials.GetValue(nameof(options.Credentials.SecretKey), String.Empty),
                    AccessKeyId = credentials.GetValue(nameof(options.Credentials.AccessKeyId), String.Empty),
                };

            }
            else
            {
                // Attempt to load Credentials from Profile
                var profileName = section.GetValue("ProfileName", String.Empty);
                if (!string.IsNullOrEmpty(profileName))
                {
                    var chain = new CredentialProfileStoreChain();
                    if (chain.TryGetProfile(profileName, out var basicProfile))
                    {
                        var awsCredentials = basicProfile.GetAWSCredentials(chain)?.GetCredentials();
                        if (awsCredentials != null)
                        {
                            options.Credentials = new AwsStorageCredentials
                            {
                                RegionEndpoint = basicProfile.Region.SystemName ?? RegionEndpoint.USEast1.SystemName,
                                SecretKey = awsCredentials.SecretKey,
                                AccessKeyId = awsCredentials.AccessKey
                            };
                        }
                    }
                }
            }

            return options;
        }
    }
}
