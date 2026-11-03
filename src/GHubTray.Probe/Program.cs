using System.Text.Json;
using System.Text.Json.Serialization;
using GHubTray.Core;

var snapshot = new LogitechDeviceReader().ReadSnapshot();
var options = new JsonSerializerOptions { WriteIndented = true };
options.Converters.Add(new JsonStringEnumConverter());
Console.WriteLine(JsonSerializer.Serialize(snapshot, options));
return snapshot.Devices.Count == 0 ? 2 : 0;
