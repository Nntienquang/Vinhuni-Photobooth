using Microsoft.AspNetCore.SignalR;
namespace Booth;
public sealed class BoothHub : Hub
{
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> Displays = new();
    public Task RegisterCustomer()
    {
        Displays[Context.ConnectionId] = 0;
        return Task.CompletedTask;
    }
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Displays.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }
}
