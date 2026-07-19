using System.Text;
using System.Text.Json;

namespace RPGGame.Shared.Network;

public static class NetworkHelper
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task SendAsync(Stream stream, object data)
    {
        string json = JsonSerializer.Serialize(data, _jsonOptions);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        byte[] lengthPrefix = BitConverter.GetBytes(jsonBytes.Length);

        await stream.WriteAsync(lengthPrefix, 0, 4);
        await stream.WriteAsync(jsonBytes, 0, jsonBytes.Length);
        await stream.FlushAsync();
    }

    public static async Task<T?> ReceiveAsync<T>(Stream stream)
    {
        return await ReceiveAsync<T>(stream, CancellationToken.None);
    }

    public static async Task<T?> ReceiveAsync<T>(Stream stream, CancellationToken token)
    {
        try
        {
            byte[] lengthBuffer = new byte[4];
            int bytesRead = 0;

            while (bytesRead < 4)
            {
                int read = await stream.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead), token);
                if (read == 0) return default;
                bytesRead += read;
            }

            int length = BitConverter.ToInt32(lengthBuffer, 0);

            if (length <= 0 || length > 1024 * 1024)
                return default;

            byte[] jsonBytes = new byte[length];
            bytesRead = 0;

            while (bytesRead < length)
            {
                int read = await stream.ReadAsync(jsonBytes.AsMemory(bytesRead, length - bytesRead), token);
                if (read == 0) return default;
                bytesRead += read;
            }

            string json = Encoding.UTF8.GetString(jsonBytes);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return default;
        }
    }
}