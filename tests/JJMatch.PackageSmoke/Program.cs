using System.Text.Json;
using JJMatch;

using var left = Load("Left");
using var right = Load("Right");
var leftApi = left.RootElement.GetProperty("publicApi");
var rightApi = right.RootElement.GetProperty("publicApi");
var result = JJMatcher.Match(Input(leftApi, "basic"), Input(rightApi, "basic"));
Require(!result.IsMatch && result.Score == 200d / 3, "Default score");
Require(result.Differences.Count == 1, "Difference count");
var difference = result.Differences[0];
Require(difference.Kind == JJDifferenceKind.ValueMismatch && difference.Path == "$.age", "Difference location");
Require(difference.LeftValue!.Value.GetInt32() == 30 && difference.RightValue!.Value.GetInt32() == 31, "Detached snapshots");
Require(JJMatcher.Match(Input(leftApi, "basic"), Input(leftApi, "basic")).IsMatch, "Identity");
var settings = new JJMatchSettings { Scoring = { Mode = JJScoringMode.Weighted } };
settings = JJMatchSettings.FromJson(settings.ToJson());
Require(JJMatcher.Match(Input(leftApi, "basic"), Input(rightApi, "basic"), settings: settings).Score == 4000d / 52, "Weighted configuration round trip");

// Exercise parsing, traversal, matching and detached difference locations at the supported depth limit.
var deepLeft = leftApi.GetProperty("depthLimit").GetString()!;
var deepRight = rightApi.GetProperty("depthLimit").GetString()!;
var deep = JJMatcher.Match(deepLeft, deepRight);
Require(!deep.IsMatch && deep.Differences.Count == 1, "Maximum depth difference");
Require(deep.Differences[0].Path == "$" + string.Concat(Enumerable.Repeat("[0]", 512)), "Maximum depth path");
Require(JJMatcher.Match(deepLeft, deepLeft).IsMatch, "Maximum depth identity");
try
{
    JJMatcher.Match(deepLeft, deepRight, settings: new JJMatchSettings { Limits = { MaxDepth = 511 } });
    throw new Exception("Expected depth rejection");
}
catch (JsonException) { }

Console.WriteLine($"Package smoke passed on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {typeof(JJMatcher).Assembly.Location}");

static JsonDocument Load(string side) => JsonDocument.Parse(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", side + ".json")),
    new JsonDocumentOptions { MaxDepth = 128 });
static string Input(JsonElement api, string name) => api.GetProperty(name).GetRawText();
static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
