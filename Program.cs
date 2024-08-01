using Amazon;
using Amazon.S3;
using Amazon.CloudFront;
using Amazon.CloudFront.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using System.Net;

public class Program
{
    public static RegionEndpoint region = RegionEndpoint.APSoutheast2;
    public static int padFilePathLogging = 50; // pad filename size for easier log reading
    public static string s3Bucket = "", s3Path = "", s3SecondaryPath = "", cloudfrontID = "";
    public static bool alsoDeleteSecondaryFile = true, alsoInvalidateCloudfrontCDN = true;
    public static IAmazonS3? s3;
    public static IAmazonCloudFront? cloudFront;

    public static async Task Main(string[] args)
    {
        // Read file names from txt file
        var fileNames = ReadLinesFromFile("FileNamesToDelete.txt", appendExtension: ".webp");

        // Establish connection to S3 and cloudfront
        await EstablishAWSConnection();

        // Log intro message
        Console.WriteLine(
            $"\nS3 & CloudFront Batch Deleter - {fileNames.Count} base file names to delete \n" +
            LineString() +
            $"Base Path : s3://{s3Bucket}/{s3Path}/\n" +
            (alsoDeleteSecondaryFile ? $"Secondary : s3://{s3Bucket}/{s3SecondaryPath}/\n" : "") +
            (alsoInvalidateCloudfrontCDN ? $"Cloudfront: cloudfront://{cloudfrontID}/{s3Path}/\n" : "") +
            (alsoInvalidateCloudfrontCDN && alsoDeleteSecondaryFile ?
                $"Cloudfront: cloudfront://{cloudfrontID}/{s3SecondaryPath}/\n" : "") +
            LineString()
        );

        // Loop through each filename found
        foreach (string fileName in fileNames)
        {
            // Delete file from s3
            await DeleteS3File(s3Path + "/" + fileName);

            // Delete secondary file
            if (alsoDeleteSecondaryFile)
                await DeleteS3File(s3SecondaryPath + "/" + fileName);

            // Delete file from cloudfront
            if (alsoInvalidateCloudfrontCDN)
                await InvalidateCloudfrontFile(s3Path + "/" + fileName);

            // Delete secondary file from cloudfront
            if (alsoInvalidateCloudfrontCDN && alsoDeleteSecondaryFile)
                await InvalidateCloudfrontFile(s3SecondaryPath + "/" + fileName);
        }

        // Log completion message
        Console.WriteLine("\nDeletion and invalidation complete.");

        // End program and clean-up
        s3!.Dispose();
    }

    // Read appsettings.json and establish S3 and Cloudfront connection
    static async Task<bool> EstablishAWSConnection()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true) //load base settings
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true) //load local settings
            .AddEnvironmentVariables()
            .Build();

        BasicAWSCredentials credentials;
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

        // Establish S3 client
        try
        {
            s3 = new AmazonS3Client(credentials, region);
            await s3.ListBucketsAsync(); // Test connection
        }
        catch (Exception e)
        {
            Console.WriteLine("Error connecting to S3 - Check IAM permissions\n\n" + e.Message);
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
        catch (Exception)
        {
            alsoInvalidateCloudfrontCDN = false;
        }

        // Establish cloudfront client
        if (alsoInvalidateCloudfrontCDN)
        {
            try
            {
                cloudFront = new AmazonCloudFrontClient(credentials, RegionEndpoint.APSoutheast2);
                await cloudFront.ListDistributionsAsync(); // Test connection
            }
            catch (Exception e)
            {
                Console.WriteLine("Error connecting to cloudfront - Check IAM permissions\n\n" + e.Message);
                throw;
            }
        }
        return true;
    }

    // Reads non empty lines from a txt file, optionally appends extension to each line, 
    // Returns as a List
    static List<string> ReadLinesFromFile(string fileName, string appendExtension = "")
    {
        try
        {
            List<string> result = new List<string>();
            string[] lines = File.ReadAllLines(@fileName);

            foreach (string line in lines)
            {
                if (line != null && !line.StartsWith("#")) result.Add(line.Trim() + appendExtension);
            }

            if (result.Count == 0)
            {
                Console.WriteLine("No lines found in " + fileName);
                Environment.Exit(0);
            }

            return result;
        }
        catch (Exception e)
        {
            throw new Exception("Unable to read file " + fileName + "\n" + e.Message);
        }
    }

    // Deletes a file from S3
    static async Task<HttpStatusCode> DeleteS3File(string filePathKey)
    {
        Console.Write($"s3://{s3Bucket}/{filePathKey}".PadRight(padFilePathLogging));

        try
        {
            // Check file exists
            var existsResponse = await s3!.GetObjectAsync(s3Bucket, filePathKey);

            // Delete file
            var response = await s3.DeleteObjectAsync(s3Bucket, filePathKey);
            Console.Write("\t - deleted\n");
            return HttpStatusCode.OK;
        }
        catch
        {
            Console.Write("\t - already deleted\n");
            return HttpStatusCode.NotFound;
        }
    }

    // Invalidates a file from Cloudfront
    static async Task<HttpStatusCode> InvalidateCloudfrontFile(string filePathKey)
    {
        Console.Write($"cloudfront://{cloudfrontID}/{filePathKey}".PadRight(padFilePathLogging));

        // Get the distribution domain
        GetDistributionResponse res =
            await cloudFront!.GetDistributionAsync(new GetDistributionRequest(cloudfrontID));

        string domainName = res.Distribution.DomainName;

        // Check if file exists on distribution
        string urlToCheck = $"https://{domainName}/{filePathKey}";
        bool fileExists = await CheckHttpFileExists(urlToCheck);
        if (!fileExists)
        {
            Console.Write("\t - already deleted\n");
            return HttpStatusCode.OK;
        }
        else
        {
            // If exists, begin invalidation
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

                if (invalidationResponse.HttpStatusCode == HttpStatusCode.Created)
                    Console.Write("\t - invalidating\n");
                else Console.Write(invalidationResponse.HttpStatusCode + "\n");

                return invalidationResponse.HttpStatusCode;

            }
            catch (AccessDeniedException e)
            {
                Console.WriteLine(
                    "Cloudfront Invalidation - IAM Role Access Denied\n" + e.Message
                );
                return HttpStatusCode.Forbidden;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return HttpStatusCode.Forbidden;
            }
        }
    }

    // Return a repeated hyphen line -------- for logging purposes
    static string LineString(char character = '-', int length = 72)
    {
        string line = "";
        for (int i = 0; i < length; i++)
        {
            line += character;
        }
        return line + "\n";
    }

    // Check if a file exists over http. Is used for cloudfront checks.
    static async Task<bool> CheckHttpFileExists(string fileUrl)
    {
        try
        {
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage response = await client.GetAsync(fileUrl);
                return response.IsSuccessStatusCode;
            }
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}