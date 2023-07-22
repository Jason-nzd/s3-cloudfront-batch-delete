using Amazon;
using Amazon.S3;
using Amazon.CloudFront;
using Amazon.CloudFront.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using System.Net;

public class Program
{
    public static string s3Bucket = "", s3Path = "", s3SecondaryPath = "", cloudfrontID = "";

    public static async Task Main(string[] args)
    {
        bool alsoDeleteSecondaryFile = true;
        bool alsoInvalidateCloudfrontCDN = true;
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
        }
        catch (System.Exception)
        {
            alsoInvalidateCloudfrontCDN = false;
        }

        // Establish S3 client
        IAmazonS3 s3 = new AmazonS3Client(credentials, RegionEndpoint.APSoutheast2);

        // Establish Cloudfront client
        IAmazonCloudFront? cloudFront = null;
        if (alsoInvalidateCloudfrontCDN)
            cloudFront = new AmazonCloudFrontClient(credentials, RegionEndpoint.APSoutheast2);

        // Find the max string length of the filenames, for logging padding purposes
        int maxStringLength = FindMaxStringLength(fileNames);

        // Loop through each filename found, delete from s3 and invalidate cloudfront
        foreach (string fileName in fileNames)
        {
            // Delete file
            string filePathKey = s3Path + fileName;
            var response = await s3.DeleteObjectAsync(s3Bucket, filePathKey);

            PrintURLStatus(
                "s3://" + s3Bucket + "/" + filePathKey,
                response.HttpStatusCode,
                maxStringLength
            );

            // Invalidate CDN
            if (alsoInvalidateCloudfrontCDN)
            {
                try
                {
                    Paths cloudfrontPath = new Paths();
                    cloudfrontPath.Items.Add("/" + filePathKey.Replace("\\", "/"));
                    cloudfrontPath.Quantity = 1;

                    CreateInvalidationRequest request =
                        new CreateInvalidationRequest(
                            cloudfrontID,
                            new InvalidationBatch(cloudfrontPath, filePathKey)
                        );
                    var invalidationResponse = await cloudFront!.CreateInvalidationAsync(request);

                    PrintURLStatus(
                        "cloudfront:/" + cloudfrontPath.Items[0],
                        invalidationResponse.HttpStatusCode,
                        maxStringLength
                    );

                }
                catch (Amazon.CloudFront.Model.AccessDeniedException e)
                {
                    Console.WriteLine(
                        "Error requesting Cloudfront Invalidation - IAM Role Access Denied\n" +
                        e.Message
                    );
                }
                catch (System.Exception)
                {
                    throw;
                }
            }

            // Delete secondary file
            if (alsoDeleteSecondaryFile)
            {
                string secondaryPathKey = s3SecondaryPath + fileName;
                var response2 = await s3.DeleteObjectAsync(s3Bucket, secondaryPathKey);

                PrintURLStatus(
                    "s3://" + s3Bucket + "/" + secondaryPathKey,
                    response2.HttpStatusCode,
                    maxStringLength
                );

                // Invalidate CDN
                if (alsoInvalidateCloudfrontCDN)
                {
                    try
                    {
                        Paths cloudfrontPath = new Paths();
                        cloudfrontPath.Items.Add("/" + secondaryPathKey.Replace("\\", "/"));
                        cloudfrontPath.Quantity = 1;

                        CreateInvalidationRequest request =
                            new CreateInvalidationRequest(
                                cloudfrontID,
                                new InvalidationBatch(cloudfrontPath, secondaryPathKey)
                            );

                        var invalidationResponse = await cloudFront!.CreateInvalidationAsync(request);

                        PrintURLStatus(
                            "cloudfront:/" + cloudfrontPath.Items[0],
                            invalidationResponse.HttpStatusCode,
                            maxStringLength
                        );
                    }
                    catch (Amazon.CloudFront.Model.AccessDeniedException e)
                    {
                        Console.WriteLine(
                            "Error requesting Cloudfront Invalidation - IAM Role Access Denied\n" +
                            e.Message
                        );
                    }
                    catch (System.Exception)
                    {
                        throw;
                    }
                }
            }
            Console.WriteLine(); // Write new line for each looped filename
        }

        // End program and clean-up
        s3.Dispose();
    }

    // Reads non empty lines from a txt file, optionally appends extension to each line, 
    // Returns as a List
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

    // Logs the URL and its deletion/invalidation status
    static void PrintURLStatus(string url, HttpStatusCode statusCode, int padding = 70)
    {
        // Pad URLs with a default padding, or +4 for over-sized URLs
        if (url.Length > padding) padding = url.Length + 4;
        string statusMessage = statusCode.ToString();

        // If the response is 'NoContent', then the s3 file has been deleted
        if (statusCode == System.Net.HttpStatusCode.NoContent) statusMessage = "Deleted";

        // If the response is 'Created', then the cloudfront invalidation has started
        else if (statusCode == System.Net.HttpStatusCode.Created) statusMessage = "Invalidating";

        Console.WriteLine(url.PadRight(padding) + " - " + statusMessage);
    }

    // Find the max string length within a list of strings
    static int FindMaxStringLength(List<string> strings)
    {
        int maxLength = 0;
        string baseUrl = "s3://" + s3Bucket + "/" + s3Path + "/200/";
        foreach (string s in strings)
        {
            if (s.Length > maxLength) maxLength = s.Length + baseUrl.Length;
        }
        return maxLength;
    }

}