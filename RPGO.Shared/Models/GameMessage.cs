using System.Text.Json.Serialization;

namespace RPGGame.Shared.Models;

public class GameMessage
{
    [JsonPropertyName("t")]
    public string Type { get; set; } = "";

    [JsonPropertyName("d")]
    public object? Data { get; set; }
}