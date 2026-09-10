using Talk2Me.Transcription;

namespace Talk2Me.Core.Tests;

public sealed class ModelStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Talk2Me.Tests." + Guid.NewGuid().ToString("N"));
    private readonly ModelStorage _storage;

    public ModelStorageTests()
    {
        Directory.CreateDirectory(_root);
        _storage = new ModelStorage(_root);

        File.WriteAllBytes(Path.Combine(_root, "ggml-base.bin"), new byte[1024]);
        File.WriteAllBytes(Path.Combine(_root, "ggml-large-v3-turbo.bin"), new byte[2048]);
        Directory.CreateDirectory(Path.Combine(_root, "parakeet-tdt-0.6b-v3-int8"));
        File.WriteAllBytes(Path.Combine(_root, "parakeet-tdt-0.6b-v3-int8", "encoder.int8.onnx"), new byte[4096]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Lists_files_and_folders_with_their_sizes()
    {
        var entries = _storage.List();

        Assert.Equal(3, entries.Count);
        Assert.Equal(2048, entries.Single(e => e.Name == "ggml-large-v3-turbo.bin").Bytes);

        var parakeet = entries.Single(e => e.IsDirectory);
        Assert.Equal("parakeet-tdt-0.6b-v3-int8", parakeet.Name);
        Assert.Equal(4096, parakeet.Bytes);
    }

    [Fact]
    public void Deletes_one_file_and_leaves_the_rest()
    {
        Assert.True(_storage.Delete("ggml-base.bin"));

        Assert.DoesNotContain(_storage.List(), e => e.Name == "ggml-base.bin");
        Assert.Equal(2048 + 4096, _storage.GetTotalBytes());
    }

    [Fact]
    public void Deletes_a_model_folder_and_everything_in_it()
    {
        Assert.True(_storage.Delete("parakeet-tdt-0.6b-v3-int8"));

        Assert.Equal(2, _storage.List().Count);
        Assert.False(Directory.Exists(Path.Combine(_root, "parakeet-tdt-0.6b-v3-int8")));
    }

    [Fact]
    public void Reports_false_for_an_entry_that_is_not_there()
    {
        Assert.False(_storage.Delete("ggml-missing.bin"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../settings.json")]
    [InlineData(@"..\..\settings.json")]
    [InlineData(@"C:\Windows\System32")]
    public void Refuses_anything_that_is_not_a_listed_entry(string name)
    {
        // Delete only ever acts on something List() reported, so a composed path cannot escape the folder.
        Assert.False(_storage.Delete(name));
        Assert.Equal(3, _storage.List().Count);
    }
}
