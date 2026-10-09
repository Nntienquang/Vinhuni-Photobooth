using Booth.Camera.Sony;
using System.Text.Json;
await using var transport = new SonyWpdTransport();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
var devices = await transport.ScanAsync(timeout.Token);
Console.WriteLine(JsonSerializer.Serialize(devices));
if (devices.Count == 0) { Console.WriteLine("SKIP: no ILCE-7M3 connected."); return; }
if (!args.Contains("--connect")) return;
await transport.OpenAsync(devices[0], timeout.Token); Console.WriteLine("CONNECTED");
Directory.CreateDirectory("data/sony-probe");
await transport.StartLiveViewAsync(timeout.Token);
for (int i = 0; i < 10; i++) { var frame = await transport.ReadLiveViewFrameAsync(timeout.Token); if (frame is not null) { Directory.CreateDirectory("data/sony-probe"); await File.WriteAllBytesAsync("data/sony-probe/live.jpg", frame); Console.WriteLine($"LIVE JPEG {frame.Length} bytes"); break; } await Task.Delay(150); }
await transport.StopLiveViewAsync(timeout.Token);
if (args.Contains("--capture")) { var photo = await transport.CaptureAsync(timeout.Token); var jpeg = await transport.DownloadCapturedJpegAsync(photo, timeout.Token); var path = $"data/sony-probe/sony_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.jpg"; await File.WriteAllBytesAsync(path, jpeg); Console.WriteLine($"CAPTURE JPEG {jpeg.Length} bytes: {path}"); }
await transport.CloseAsync(); Console.WriteLine("DISCONNECTED");
