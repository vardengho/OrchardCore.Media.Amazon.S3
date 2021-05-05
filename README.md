# OrchardCore.Media.Amazon.S3
### Amazon S3 bucket provider for OrchardCore

[![CI](https://github.com/neglectedvalue/OrchardCore.Media.Amazon.S3/actions/workflows/CI.yml/badge.svg?branch=main)](https://github.com/neglectedvalue/OrchardCore.Media.Amazon.S3/actions/workflows/CI.yml)

To use this feature you should enable 'OrchardCore.Media.Amazon.S3' in the Orchard admin panel.
***
Credentials section needed only if Orchard will be hosted not in the AWS Cloud since in the AWS you dont need to store this info in the appsettings file, you just need to set BuckeName.
***
Example of the configuration in the appsettings.json :

```
"OrchardCore": {
    "OrchardCore_Media_Amazon_S3": {
        "Credentials": {
        "SecretKey": "",
        "AccessKeyId": "",
        "RegionEndpoint": ""
        },
        "BasePath": "/",
        "BucketName": ""
    }
}
```
