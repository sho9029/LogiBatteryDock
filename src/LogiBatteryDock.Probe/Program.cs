using System.Text.Json;
using System.Text.Json.Serialization;
using LogiBatteryDock.Core;

var ghubPort = GHubAgentTelemetryReader.DefaultPort;
for (var index = 0; index < args.Length - 1; index++)
{
    if (string.Equals(args[index], "--ghub-port", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(args[index + 1], out var parsedPort))
    {
        ghubPort = parsedPort;
        break;
    }
}

var reader = ghubPort == GHubAgentTelemetryReader.DefaultPort
    ? new GHubAwareTelemetryReader()
    : new GHubAwareTelemetryReader(ghubPort);
var snapshot = reader.ReadSnapshot();
var options = new JsonSerializerOptions { WriteIndented = true };
options.Converters.Add(new JsonStringEnumConverter());
Console.WriteLine(JsonSerializer.Serialize(snapshot, options));
return snapshot.Devices.Count == 0 ? 2 : 0;
