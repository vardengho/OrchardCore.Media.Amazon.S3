using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace OrchardCore.FileStorage.Amazon.S3
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using FileStorage;
    using Modules;
    
    public class AwsFileStore : IFileStore
    {
        private readonly IClock _clock;
        private readonly AwsStorageOptions _options;
        private readonly string _basePrefix = null;
        private readonly IAmazonS3 _amazonS3Client;

        public AwsFileStore(IClock clock, AwsStorageOptions options, IAmazonS3 amazonS3Client)
        {
            _clock = clock;
            _options = options;
            _amazonS3Client = amazonS3Client;
            
            if (!string.IsNullOrEmpty(_options.BasePath))
            {
                _basePrefix = NormalizePrefix(_options.BasePath);
            }
        }
        
        public async Task<IFileStoreEntry> GetFileInfoAsync(string path)
        {
            try
            {
                var objectMetadata = await _amazonS3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _options.BucketName,
                    Key = this.Combine(_basePrefix, path)
                });
                
                return new AwsFile(path, objectMetadata.ContentLength, objectMetadata.LastModified);
            }
            // Bucket or file does not exist
            catch (AmazonS3Exception)
            {
                return null;
            }
        }

        public async Task<IFileStoreEntry> GetDirectoryInfoAsync(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return new AwsDirectory(path, _clock.UtcNow);
            }

            var awsDirectory = await _amazonS3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _options.BucketName,
                Prefix = NormalizePrefix(this.Combine(_basePrefix, path)),
                MaxKeys = 1,
                FetchOwner = false
            });

            if (awsDirectory.S3Objects.Any())
            {
                return new AwsDirectory(path, _clock.UtcNow);
            }

            return null;
        }

        // TODO: Think about changing method signature to AsyncEnumerable
        public async Task<IEnumerable<IFileStoreEntry>> GetDirectoryContentAsync(string path = null, bool includeSubDirectories = false)
        {
            var listObjectsResponse = await _amazonS3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _options.BucketName,
                Delimiter = "/",
                Prefix = NormalizePrefix(this.Combine(_basePrefix, path)),
                FetchOwner = false,
            });

            var results = new List<IFileStoreEntry>();
            foreach (var file in listObjectsResponse.S3Objects)
            {
                var itemName = Path.GetFileName(WebUtility.UrlDecode(file.Key));

                if (includeSubDirectories || !string.IsNullOrEmpty(itemName))
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        path = "/";
                    }
                    var itemPath = this.Combine(path, itemName);
                    results.Add(new AwsFile(itemPath, file.Size, file.LastModified));
                }
            }

            foreach (var awsFolderPath in listObjectsResponse.CommonPrefixes)
            {
                var folderPath = awsFolderPath;
                if (!string.IsNullOrEmpty(_basePrefix))
                {
                    folderPath = folderPath.Substring(_basePrefix.Length - 1);
                }
                
                folderPath = folderPath.TrimEnd('/');
                results.Add(new AwsDirectory(folderPath, _clock.UtcNow));
            }
            
            return results
                .OrderByDescending(x => x.IsDirectory)
                .ToArray();
        }

        public async Task<bool> TryCreateDirectoryAsync(string path)
        {
            try
            {
                await _amazonS3Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = NormalizePrefix(this.Combine(_basePrefix, path))
                });
                
                return true;
            }
            catch (AmazonS3Exception)
            {
                return false;
            }
        }

        public async Task<bool> TryDeleteFileAsync(string path)
        {
            try
            {
                await _amazonS3Client.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = this.Combine(_basePrefix, path)
                });
                
                return true;
            }
            catch (AmazonS3Exception)
            {
                return false;
            }
        }

        public async Task<bool> TryDeleteDirectoryAsync(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new FileStoreException("Cannot delete the root directory.");
            }

            var listObjectsResponse = await _amazonS3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _options.BucketName,
                Prefix = NormalizePrefix(this.Combine(_basePrefix, path))
            });
            
            var deleteObjectsRequest = new DeleteObjectsRequest
            {
                BucketName = _options.BucketName,
                Objects = listObjectsResponse.S3Objects
                    .Select( key => new KeyVersion { Key = key.Key }).ToList()
            };

            await _amazonS3Client.DeleteObjectsAsync(deleteObjectsRequest);

            return true;
        }

        public async Task MoveFileAsync(string oldPath, string newPath)
        {
            await CopyFileAsync(oldPath, newPath);
            await TryDeleteFileAsync(oldPath);
        }

        public async Task CopyFileAsync(string srcPath, string dstPath)
        {
            if (srcPath == dstPath)
            {
                throw new ArgumentException($"The values for {nameof(srcPath)} and {nameof(dstPath)} must not be the same.");
            }

            try
            {
                await _amazonS3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _options.BucketName,
                    Key = this.Combine(_basePrefix, srcPath)
                });
            }
            catch (AmazonS3Exception)
            {
                throw new FileStoreException($"Cannot copy file '{srcPath}' because it does not exist.");
            }

            try
            {
                var listObjects = await _amazonS3Client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = _options.BucketName,
                    Prefix = this.Combine(_basePrefix, dstPath)
                });

                if (listObjects.S3Objects.Any())
                {
                    throw new FileStoreException($"Cannot copy file '{srcPath}' because a file already exists in the new path '{dstPath}'.");   
                }

                var copyObjectResponse = await _amazonS3Client.CopyObjectAsync(new CopyObjectRequest
                {
                    SourceBucket = _options.BucketName,
                    SourceKey = this.Combine(_basePrefix, srcPath),
                    DestinationBucket = _options.BucketName,
                    DestinationKey = this.Combine(_basePrefix, dstPath)
                });

                if (copyObjectResponse.HttpStatusCode != HttpStatusCode.OK)
                {
                    throw new FileStoreException($"Error while copying file '{srcPath}'");
                }

            }
            catch (AmazonS3Exception)
            {
                throw new FileStoreException($"Error while copying file '{srcPath}'");
            }
        }

        public async Task<Stream> GetFileStreamAsync(string path)
        {
            try
            {
                var transferUtility = new TransferUtility(_amazonS3Client);
                return await transferUtility.OpenStreamAsync(_options.BucketName, this.Combine(_basePrefix, path));
            }
            catch (AmazonS3Exception)
            {
                throw new FileStoreException($"Cannot get file stream because the file '{path}' does not exist.");
            }
        }

        public async Task<Stream> GetFileStreamAsync(IFileStoreEntry fileStoreEntry)
        {
            return await GetFileStreamAsync(fileStoreEntry.Path);
        }

        public async Task<string> CreateFileFromStreamAsync(string path, Stream inputStream, bool overwrite = false)
        {
            try
            {
                if (!overwrite)
                {
                    var listObjects = await _amazonS3Client.ListObjectsV2Async(new ListObjectsV2Request
                    {
                        BucketName = _options.BucketName,
                        Prefix = this.Combine(_basePrefix, path)
                    });

                    if (listObjects.S3Objects.Any())
                    {
                        throw new FileStoreException($"Cannot create file '{path}' because it already exists.");    
                    }
                }

                var response = await _amazonS3Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = this.Combine(_basePrefix, path),
                    InputStream = inputStream
                });

                if (response.HttpStatusCode != HttpStatusCode.OK)
                {
                    throw new FileStoreException($"Cannot create file '{path}'");
                }
            }
            catch (AmazonS3Exception ex)
            {
                throw new FileStoreException($"Cannot create file '{path}', S3 service threw an exception: {ex.Message}");
            }

            return path;
        }

        private string NormalizePrefix(string prefix)
        {
            prefix = prefix.Trim('/') + '/';
            if (prefix.Length == 1)
            {
                return String.Empty;
            }

            return prefix;
        }
    }
}