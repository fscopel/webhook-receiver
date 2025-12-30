using Microsoft.AspNetCore.SignalR;
using WebhookReceiver.Models;
using WebhookReceiver.Services;

namespace WebhookReceiver.Hubs;

public class WebhookHub : Hub
{
    private readonly WebhookStore _store;

    public WebhookHub(WebhookStore store)
    {
        _store = store;
    }

    public override async Task OnConnectedAsync()
    {
        // Send all existing entries to the newly connected client
        var entries = _store.GetAll();
        await Clients.Caller.SendAsync("InitialData", entries);
        await base.OnConnectedAsync();
    }

    public async Task DeleteEntry(string id)
    {
        if (_store.Delete(id))
        {
            await Clients.All.SendAsync("EntryDeleted", id);
        }
    }

    public async Task ClearAll()
    {
        _store.Clear();
        await Clients.All.SendAsync("AllCleared");
    }
}

