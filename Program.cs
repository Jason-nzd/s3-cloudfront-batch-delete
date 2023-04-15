﻿using Amazon;
using Amazon.S3;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;

public class Program
{
    public static async Task Main(string[] args)
    {
        bool alsoDeleteSecondaryFile = true;
        bool alsoInvalidateCloudfrontCDN = true;
        string s3Bucket, s3Path, s3SecondaryPath = "", cloudfrontID, invalidationBaseCmd = "";
        BasicAWSCredentials credentials;

        // Read file names from txt file
        var fileNames = ReadLinesFromFile("ids.txt", appendExtension: ".webp");

        // Get config from appsettings.json
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        try
        {
            // Set AWS credentials
            credentials = new BasicAWSCredentials(
                config.GetRequiredSection("AWS_ACCESS_KEY").Get<string>(),
                config.GetRequiredSection("AWS_SECRET_KEY").Get<string>()
            );

            // Get S3 bucket name, S3 paths, and cloudfront distribution ID
            s3Bucket = config.GetRequiredSection("S3_BUCKET").Get<string>()!;
            s3Path = config.GetRequiredSection("S3_PATH").Get<string>()!;
        }
        catch (Exception)
        {
            Console.Write(
                "\nError reading appsettings.json. Be sure to create:\nappsettings.json\n" +
                "{\n" +
                "\t\"AWS_ACCESS_KEY\": \"<your access key>\",\n" +
                "\t\"AWS_SECRET_KEY\": \"<your secret key>\",\n" +
                "\t\"S3_BUCKET\": \"<your s3 bucket>\",\n" +
                "\t\"S3_PATH\": \"<your s3 path>\"\n" +
                "}\n\n"
            );
            throw;
        }

        // Try get optional s3 secondary path
        try
        {
            s3SecondaryPath = config.GetRequiredSection("S3_SECONDARY_PATH").Get<string>()!;
        }
        catch (Exception)
        {
            alsoDeleteSecondaryFile = false;
        }

        // Try get optional cloudfront id
        try
        {
            cloudfrontID = config.GetRequiredSection("CDN_DISTRIBUTION_ID").Get<string>()!;
            invalidationBaseCmd =
                    "aws cloudfront create-invalidation --distribution-id " +
                    cloudfrontID + " --paths \"/";
        }
        catch (System.Exception)
        {
            alsoInvalidateCloudfrontCDN = false;
        }

        // Establish S3
        IAmazonS3 s3 = new AmazonS3Client(credentials, RegionEndpoint.APSoutheast2);

        // Loop through each filename found, delete from s3 and invalidate cloudfront
        foreach (string fileName in fileNames)
        {
            // Delete file
            string filePathKey = s3Path + fileName;
            var response = await s3.DeleteObjectAsync(s3Bucket, filePathKey);
            Console.WriteLine(
                $"s3://" + s3Bucket + "/" + filePathKey + " - " +
                ((response.HttpStatusCode == System.Net.HttpStatusCode.NoContent) ?
                    "Deleted" : response.HttpStatusCode.ToString())
            );

            // Invalidate CDN
            if (alsoInvalidateCloudfrontCDN)
            {
                Console.WriteLine(invalidationBaseCmd + filePathKey + "\"");
            }

            // Delete secondary file
            if (alsoDeleteSecondaryFile)
            {
                string secondaryPathKey = s3SecondaryPath + fileName;
                var response2 = await s3.DeleteObjectAsync(s3Bucket, secondaryPathKey);
                Console.WriteLine(
                $"s3://" + s3Bucket + "/" + secondaryPathKey + " - " +
                ((response2.HttpStatusCode == System.Net.HttpStatusCode.NoContent) ?
                    "Deleted" : response.HttpStatusCode.ToString())
            );


                // Invalidate CDN
                if (alsoInvalidateCloudfrontCDN)
                {
                    Console.WriteLine(invalidationBaseCmd + secondaryPathKey + "\"");
                }
            }
        }

        // End program and clean-up
        s3.Dispose();
    }

    // Reads non empty lines from a txt file, optionally appends extension to each line, then return as a List
    public static List<string> ReadLinesFromFile(string fileName, string appendExtension = "")
    {
        try
        {
            List<string> result = new List<string>();
            string[] lines = File.ReadAllLines(@fileName);

            if (lines.Length == 0) throw new Exception("No lines found in " + fileName);

            foreach (string line in lines)
            {
                if (line != null) result.Add(line.Trim() + appendExtension);
            }
            return result;
        }
        catch (System.Exception e)
        {
            throw new Exception("Unable to read file " + fileName + "\n" + e.Message);
        }
    }
}