# S3 & Cloudfront Batch Delete

This .NET console app reads a text file of filenames to be deleted from an AWS S3 Bucket. It is used for batch processing of large amount of files that would otherwise take too long to delete one by one.

Thumbnail images or other secondary files can be optionally deleted alongside the main files.

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

AWS Credentials will need to have IAM permissions to list and delete from S3, and optionally have invalidation permission for Cloudfront.

## Example Input

An example text file `FileNamesToDelete.txt` containing:

```txt
122402.jpg
543212.jpg
file3.pdf
file4.png
```

Would result in 4 files being batch deleted from the S3 bucket and path specified in `appsettings.json`.

If a `S3_SECONDARY_PATH` is set, any files with the same filename will be deleted from S3. This is useful for thumbnail images with the same filename, but different paths.

If `CDN_DISTRIBUTION_ID` is set, any Cloudfront CDN associated files will also be invalidated. The ID can be obtained from `https://us-east-1.console.aws.amazon.com/cloudfront`.
