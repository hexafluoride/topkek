using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeimdallBase;

namespace Chatter;

public class MisskeyUtil
{
    private readonly HttpClient HttpClient;
    private readonly GptUtil GptUtil; 

    private string Hostname => Config.GetString("misskey.host");
    private string AccessKey => Config.GetString("misskey.key");
    
    public MisskeyUtil(GptUtil gptUtil)
    {
        HttpClient = new();
        GptUtil = gptUtil;
    }

    readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<string?> UploadFile(byte[] contents, string name, string mimeType)
    {
        // var requestParams = new
        // {
        //     i = AccessKey,
        //     name,
        //     isSensitive = true
        // };

        // var requestString = JsonSerializer.Serialize(requestParams, SerializerOptions);
        // Console.WriteLine($"Request: {requestString}");
        var content = new MultipartFormDataContent();

        var form = new List<(string, string)>();
        form.Add(("i", AccessKey));
        form.Add(("name", name));
        form.Add(("isSensitive", "false"));

        // var formContent =
        //     new FormUrlEncodedContent(form.Select(p => new KeyValuePair<string, string>(p.Item1, p.Item2)));
        var fileContent = new ByteArrayContent(contents);
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") {FileName = $"{name}", Name = $"{name}"};
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        
        content.Add(fileContent, name, mimeType);
        // content.Add(formContent);

        foreach (var (key, value) in form)
        {
            var propertyContent = new StringContent(value, Encoding.UTF8, "text/plain");
            
            propertyContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") {Name = $"{key}"};
            propertyContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            content.Add(new StringContent(value, Encoding.UTF8, "text/plain"), key);
        }
        
        var response = await HttpClient.PostAsync($"{Hostname}/drive/files/create", content);
        Console.WriteLine(response.StatusCode);
        
        var responseString = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"resp string: {responseString}");
        // var responseObject = (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync())).RootElement;
        var responseObject = JsonDocument.Parse(responseString).RootElement;
        if (responseObject.TryGetProperty("error", out JsonElement errorObject))
        {
            if (errorObject.TryGetProperty("message", out JsonElement messageElement))
            {
                Console.WriteLine($"Failed with code {response.StatusCode} message {messageElement.GetString() ?? "null"}");
            }
            else
            {
                Console.WriteLine($"Failed with code {response.StatusCode}");
            }

            return null;
        }

        if (responseObject.TryGetProperty("id", out JsonElement idProperty))
        {
            return idProperty.GetString();
        }

        Console.WriteLine($"No id property, response {responseObject.ToString()}");
        return null;
    }
    
    public async Task<string?> PostAsync(string contents, string? fileId = null)
    {
        var requestParams = new
        {
            i = AccessKey,
            text = contents,
            fileIds = fileId is null ? null : new [] { fileId }
        };

        var requestString = JsonSerializer.Serialize(requestParams, SerializerOptions);
        Console.WriteLine($"Request: {requestString}");
        var response = await HttpClient.PostAsync($"{Hostname}/notes/create",
            new StringContent(requestString, encoding: Encoding.UTF8, mediaType: "application/json"));
        
        var responseObject = (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync())).RootElement;
        if (responseObject.TryGetProperty("error", out JsonElement errorObject))
        {
            if (errorObject.TryGetProperty("message", out JsonElement messageElement))
            {
                Console.WriteLine($"Failed with code {response.StatusCode} message {messageElement.GetString() ?? "null"}");
            }
            else
            {
                Console.WriteLine($"Failed with code {response.StatusCode}");
            }

            return null;
        }

        if (!responseObject.TryGetProperty("createdNote", out JsonElement createdNote))
        {
            Console.WriteLine($"No createdNote, response {responseObject.ToString()}");
            return null;
        }

        if (createdNote.TryGetProperty("id", out JsonElement idProperty))
        {
            return idProperty.GetString();
        }

        Console.WriteLine($"No id property, response {responseObject.ToString()}");
        return null;
    }
}