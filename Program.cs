using Amazon;
using Amazon.S3;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;

public class Program
{
    public static async Task Main(string[] args)
    {
        string s3bucket = "supermarketimages";
        string s3path = "product-images/";

        // Get AWS credentials from appsettings.json
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
        BasicAWSCredentials credentials = new BasicAWSCredentials(
            config.GetRequiredSection("AWS_ACCESS_KEY").Get<string>(),
            config.GetRequiredSection("AWS_SECRET_KEY").Get<string>()
        );

        IAmazonS3 s3 = new AmazonS3Client(credentials, RegionEndpoint.APSoutheast2);

        // var test = await s3.GetBucketLocationAsync(s3bucket);
        // Console.WriteLine(test.Location);

        // Read file names from txt file
        var fileNames = ReadLinesFromFile("ids.txt", appendExtension: ".webp");

        foreach (string fileName in fileNames)
        {
            string filePathKey = s3path + fileName;
            string thumbKey = s3path + "200/" + fileName;

            var response = await s3.DeleteObjectAsync(s3bucket, filePathKey);
            Console.WriteLine("s3://" + s3bucket + "/" + filePathKey.PadRight(40) + " - " + response.HttpStatusCode);

            var response2 = await s3.DeleteObjectAsync(s3bucket, thumbKey);
            Console.WriteLine("s3://" + s3bucket + "/" + thumbKey.PadRight(40) + " - " + response2.HttpStatusCode);
        }
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