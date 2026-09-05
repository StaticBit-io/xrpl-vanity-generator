using System.IO;
using XrplVanity.Cli;
using XrplVanity.Core;
using Xunit;

namespace XrplVanity.Tests;

public class CliMatchWriterTests
{
    private static Match M(int i) => new($"rAddr{i}", $"sEdSeed{i}", "Addr", MatchPosition.Starts);

    [Fact]
    public void Writes_The_Stdout_Line_And_Appends_Address_And_Seed_To_The_Out_File()
    {
        string path = Path.GetTempFileName();
        try
        {
            StringWriter stdout = new();
            using (CliMatchWriter writer = new(path, stdout))
            {
                writer.Write(M(7));
            }

            Assert.Equal("Address: rAddr7\tSeed: sEdSeed7", stdout.ToString().Trim());
            Assert.Equal("rAddr7\tsEdSeed7", File.ReadAllText(path).Trim());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Without_An_Out_File_Only_Stdout_Is_Written()
    {
        StringWriter stdout = new();
        using CliMatchWriter writer = new(null, stdout);
        writer.Write(M(1));
        Assert.Contains("rAddr1", stdout.ToString());
    }
}
