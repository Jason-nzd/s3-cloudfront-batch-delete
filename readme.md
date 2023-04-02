# S3 Batch Delete

This .NET console app reads a text file of filenames to be deleted from an AWS S3 Bucket. It is used for batch processing of large amount of files that would otherwise take too long to delete one by one.

Thumbnail images or other secondary files can be optionally deleted alongside the main file.

A related Cloudfront CDN can also invalidate its cache of the deleted S3 files.

## Setup

`appsettings.json` needs to be created with the following variables:

```json
{
  "AWS_ACCESS_KEY": "",
  "AWS_SECRET_KEY": "",
  "S3_BUCKET": "",
  "S3_PATH": "",
  "S3_SECONDARY_PATH": "<optional>",  
  "CDN_DISTRIBUTION_ID": "<optional>"
}
```

## Sample

An input `ids.txt` text file containing:

```txt
122402
543212
```

Would result in the following commands being run:

```c#
s3.DeleteObjectAsync(s3Bucket, 122402);
aws cloudfront create-invalidation --distribution-id E1234 --paths "s3path/122402"
s3.DeleteObjectAsync(s3Bucket, thumbnail/122402);
aws cloudfront create-invalidation --distribution-id E1234 --paths "s3path/thumbnail/122402"

s3.DeleteObjectAsync(s3Bucket, 543212);
aws cloudfront create-invalidation --distribution-id E1234 --paths "s3path/543212"
s3.DeleteObjectAsync(s3Bucket, thumbnail/543212);
aws cloudfront create-invalidation --distribution-id E1234 --paths "s3path/thumbnail/543212"
```

## Todo

- Switch cloudfront invalidation from aws command-line to .net sdk.
- Simplify log output
