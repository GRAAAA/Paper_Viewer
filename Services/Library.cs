using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PaperView;

public sealed class RecentPdf
{
    public string FilePath { get; set; } = "";
    public DateTime LastOpened { get; set; }
    public uint PageCount { get; set; }
    public uint LastPage { get; set; }
    public long Size { get; set; }
    [JsonIgnore] public string Name => Path.GetFileName(FilePath);
    [JsonIgnore] public string Details => $"PDF  ·  {PageCount} pages  ·  {(Size >= 1048576 ? $"{Size / 1048576d:0.0} MB" : $"{Size / 1024d:0} KB")}";
    [JsonIgnore] public string ViewedLabel => $"Opened {LastOpened.ToLocalTime():MMM d, h:mm tt}";
    [JsonIgnore] public BitmapImage? Thumbnail { get; set; }
}

public sealed class Library
{
    public string Root { get; }
    public List<RecentPdf> Items { get; private set; } = [];
    public string? LoadWarning { get; private set; }
    private bool storageAvailable = true;
    private string HistoryPath => Path.Combine(Root, "history.json");
    public Library(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperView");
        try { Directory.CreateDirectory(Path.Combine(Root, "thumbnails")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            storageAvailable = false;
            LoadWarning = "History storage is unavailable. You can read PDFs, but recent files will not be saved.";
            return;
        }
        try
        {
            if (File.Exists(HistoryPath))
                Items = (JsonSerializer.Deserialize<List<RecentPdf>>(File.ReadAllText(HistoryPath)) ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x.FilePath)).OrderByDescending(x => x.LastOpened).Take(100).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { LoadWarning = "Could not read saved history. Your PDFs are unaffected."; }
        foreach (var item in Items)
        {
            try { if (File.Exists(ThumbnailPath(item.FilePath))) item.Thumbnail = ReadImage(File.ReadAllBytes(ThumbnailPath(item.FilePath))); }
            catch (Exception) { /* A damaged cached thumbnail must not prevent startup. */ }
        }
    }
    public string ThumbnailPath(string path) => Path.Combine(Root, "thumbnails", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))) + ".png");
    public void Save()
    {
        if (!storageAvailable) throw new IOException("The history folder is unavailable.");
        var temp = HistoryPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, HistoryPath, true);
    }
    public void Remove(RecentPdf item)
    {
        Items.Remove(item);
        Save();
        try { File.Delete(ThumbnailPath(item.FilePath)); } catch (IOException) { }
    }
    public void Clear()
    {
        var paths = Items.Select(item => ThumbnailPath(item.FilePath)).ToArray();
        Items.Clear();
        Save();
        foreach (var path in paths)
            try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    public static BitmapImage ReadImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }
}

public static class PdfRenderer
{
    public static async Task<PdfDocument> OpenAsync(string path) => await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
    public static async Task<byte[]> RenderAsync(PdfDocument document, uint index, uint width)
    {
        using var page = document.GetPage(index);
        using var stream = new InMemoryRandomAccessStream();
        // Bound both dimensions for unusually tall or wide documents.
        var scale = Math.Min(Math.Min(width / page.Size.Width, 4096 / page.Size.Height), 4096 / page.Size.Width);
        await page.RenderToStreamAsync(stream, new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, page.Size.Width * scale),
            DestinationHeight = (uint)Math.Max(1, page.Size.Height * scale)
        });
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var bytes = new byte[checked((int)stream.Size)];
        await reader.LoadAsync((uint)bytes.Length); reader.ReadBytes(bytes);
        return bytes;
    }
}
