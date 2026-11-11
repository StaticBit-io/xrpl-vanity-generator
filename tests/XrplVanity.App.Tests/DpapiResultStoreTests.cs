using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using XrplVanity.App.Core.Localization;
using XrplVanity.App.Core.Storage;
using Xunit;

namespace XrplVanity.App.Tests;

public class DpapiResultStoreTests
{
    private static string TempFolder() => Directory.CreateTempSubdirectory("xrpl-vanity-results-").FullName;

    private static void RequireWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Skip("DPAPI is Windows-only");
        }
    }

    [Fact]
    public void Round_Trips_And_The_File_Does_Not_Contain_The_Seed_In_Clear()
    {
        RequireWindows();
        string folder = TempFolder();
        DpapiResultStore store = new(folder);
        StoredResult result = new("rAddr1", "sEdSeedSecret", "abc", "Starts", DateTimeOffset.UtcNow, "cuda:0");

        store.Append(result);

        byte[] raw = File.ReadAllBytes(Path.Combine(folder, "results.dat"));
        Assert.DoesNotContain("sEdSeedSecret", System.Text.Encoding.UTF8.GetString(raw));
        Assert.Equal(new[] { result }, new DpapiResultStore(folder).LoadAll());
    }

    [Fact]
    public void Delete_And_Clear_Rewrite_The_File()
    {
        RequireWindows();
        DpapiResultStore store = new(TempFolder());
        store.Append(new("rA", "s1", "a", "Starts", DateTimeOffset.UtcNow, "cpu"));
        store.Append(new("rB", "s2", "b", "Starts", DateTimeOffset.UtcNow, "cpu"));

        store.Delete("rA");
        Assert.Single(store.LoadAll());
        store.Clear();
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void DeleteMany_Removes_Every_Named_Address_And_Keeps_The_Rest()
    {
        RequireWindows();
        string folder = TempFolder();
        DpapiResultStore store = new(folder);
        store.Append(new StoredResult("rA", "s1", "a", "Starts", DateTimeOffset.UtcNow, "cpu"));
        store.Append(new StoredResult("rB", "s2", "b", "Starts", DateTimeOffset.UtcNow, "cpu"));
        store.Append(new StoredResult("rC", "s3", "c", "Starts", DateTimeOffset.UtcNow, "cpu"));

        store.DeleteMany(new[] { "rA", "rC" });

        Assert.Equal(new[] { "rB" }, store.LoadAll().Select(r => r.Address).ToArray());
        Assert.Equal(new[] { "rB" }, new DpapiResultStore(folder).LoadAll().Select(r => r.Address).ToArray());
    }

    [Fact]
    public void DeleteMany_Ignores_An_Address_That_Is_Not_Stored()
    {
        RequireWindows();
        string folder = TempFolder();
        DpapiResultStore store = new(folder);
        store.Append(new StoredResult("rA", "s1", "a", "Starts", DateTimeOffset.UtcNow, "cpu"));

        store.DeleteMany(new[] { "rZ" });

        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void DeleteMany_With_Nothing_To_Delete_Leaves_The_Store_Alone()
    {
        RequireWindows();
        string folder = TempFolder();
        DpapiResultStore store = new(folder);
        store.Append(new StoredResult("rA", "s1", "a", "Starts", DateTimeOffset.UtcNow, "cpu"));

        store.DeleteMany(Array.Empty<string>());

        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void A_Corrupted_File_Makes_The_Store_Read_Only_With_A_Reason()
    {
        RequireWindows();
        string folder = TempFolder();
        File.WriteAllBytes(Path.Combine(folder, "results.dat"), new byte[] { 1, 2, 3 });
        DpapiResultStore store = new(folder);

        Assert.Empty(store.LoadAll());
        Assert.True(store.IsReadOnly);
        Assert.NotNull(store.ReadOnlyReason);
        Assert.Throws<InvalidOperationException>(() => store.Append(new("rA", "s", "a", "Starts", DateTimeOffset.UtcNow, "cpu")));
    }

    [Fact]
    public void DeleteMany_On_A_Read_Only_Store_Throws_Even_With_Nothing_To_Delete()
    {
        RequireWindows();
        string folder = TempFolder();
        File.WriteAllBytes(Path.Combine(folder, "results.dat"), new byte[] { 1, 2, 3 });
        DpapiResultStore store = new(folder);

        // The store reads results.dat lazily, so it only learns it is read-only on first load.
        Assert.Empty(store.LoadAll());
        Assert.True(store.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => store.DeleteMany(new[] { "rA" }));
        Assert.Throws<InvalidOperationException>(() => store.DeleteMany(Array.Empty<string>()));
    }

    [Fact]
    public void A_Failed_Write_Leaves_The_Cache_And_Disk_Untouched_And_Turns_The_Store_Read_Only()
    {
        RequireWindows();
        string folder = TempFolder();
        DpapiResultStore store = new(folder);
        StoredResult first = new("rFirst", "sEdFakeSeedOne", "a", "Starts", DateTimeOffset.UtcNow, "cpu");
        store.Append(first);

        Directory.CreateDirectory(Path.Combine(folder, "results.dat.tmp"));

        Assert.Throws<InvalidOperationException>(() => store.Append(new("rSecond", "sEdFakeSeedTwo", "b", "Starts", DateTimeOffset.UtcNow, "cpu")));
        Assert.True(store.IsReadOnly);
        Assert.Equal(HistoryText.ResultsCouldNotBeWritten, store.ReadOnlyReason!.Id);
        Assert.Equal(new[] { first }, store.LoadAll());
        Assert.Equal(new[] { first }, new DpapiResultStore(folder).LoadAll());
    }
}
