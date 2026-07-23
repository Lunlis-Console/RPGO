using System.Text.Json;
using System.Text.Json.Serialization;

namespace RPGGame.Shared.Models;

public class DialogueTree
{
    [JsonPropertyName("nodes")]
    public Dictionary<string, DialogueNode> Nodes { get; set; } = new();
}

public class DialogueNode
{
    [JsonPropertyName("speaker")]
    public string Speaker { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("choices")]
    public List<DialogueChoice> Choices { get; set; } = new();
}

public class DialogueChoice
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("next")]
    public string? NextNodeId { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("condition")]
    public string? Condition { get; set; }
}

public static class DialogueParser
{
    public static DialogueTree? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var tree = JsonSerializer.Deserialize<DialogueTree>(json, opts);
            if (tree != null && tree.Nodes.Count > 0)
                return tree;

            var flat = JsonSerializer.Deserialize<Dictionary<string, DialogueNode>>(json, opts);
            if (flat != null && flat.Count > 0)
                return new DialogueTree { Nodes = flat };

            return null;
        }
        catch
        {
            return null;
        }
    }
}
