using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PackEngine.Workspace;

namespace PackEngine.Assistant.Api;

// Official Images API only. A ChatGPT login or skill name is never treated as an API credential.
public sealed class OpenAiImages : IDisposable
{
    private readonly HttpClient http;
    public OpenAiImages() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }
    public OpenAiImages(HttpMessageHandler handler) { http = new(handler) { Timeout = TimeSpan.FromMinutes(6) }; }
    public async Task<byte[]> Generate(ImageAiConnection settings, string key, string prompt, string size, bool transparent, CancellationToken cancellation)
    {
        settings.Validate();
        if (!settings.Enabled || string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Image generation is not configured. Use the editor's image connection setup.");
        if (prompt.Length == 0 || prompt.Length > 32000 || size is not ("1024x1024" or "1536x1024" or "1024x1536")) throw new ArgumentException("Choose a prompt and a supported image size.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/generations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(EditorSession.Serialize(new { model = settings.Model, prompt, n = 1, size, quality = settings.Quality, output_format = "png", background = transparent ? "transparent" : "opaque" }), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("OpenAI image generation returned HTTP " + (int)response.StatusCode + ". Check the API key, model access and usage limit in image setup.");
        if (response.Content.Headers.ContentLength > 45_000_000) throw new InvalidDataException("Image response exceeded 45 MB.");
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false); using var data = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
        { if (data.Length + count > 45_000_000) throw new InvalidDataException("Image response exceeded 45 MB."); data.Write(buffer, 0, count); }
        using var json = JsonDocument.Parse(data.ToArray());
        var images = json.RootElement.GetProperty("data");
        if (images.GetArrayLength() != 1 || !images[0].TryGetProperty("b64_json", out var encoded)) throw new InvalidDataException("The API did not return one encoded PNG. No download URL was followed.");
        byte[] bytes = Convert.FromBase64String(encoded.GetString()!); ValidatePng(bytes); cancellation.ThrowIfCancellationRequested(); return bytes;
    }
    public static void ValidatePng(byte[] bytes)
    {
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Length < 33 || bytes.Length > 32_000_000 || !bytes.Take(8).SequenceEqual(signature) || Encoding.ASCII.GetString(bytes, 12, 4) != "IHDR") throw new InvalidDataException("Expected a PNG up to 32 MB.");
        long Dimension(int offset) => ((long)bytes[offset] << 24) | ((long)bytes[offset + 1] << 16) | ((long)bytes[offset + 2] << 8) | bytes[offset + 3];
        long width = Dimension(16), height = Dimension(20);
        if (width < 1 || height < 1 || width > 8192 || height > 8192 || width * height > 16_000_000) throw new InvalidDataException("PNG dimensions exceed the native preview limit.");
    }
    public void Dispose() => http.Dispose();
}
