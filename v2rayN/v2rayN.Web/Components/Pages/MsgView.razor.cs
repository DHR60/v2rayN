using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using v2rayN.Web.Maneger;

namespace v2rayN.Web.Components.Pages;

public partial class MsgView
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    public MsgView()
    {
        ViewModel = SingletonManager.Instance.MsgViewModel;
    }

    private async void CopyAllMessages()
    {
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", SingletonManager.Instance.MsgViewContent);
    }

    private void ClearAllMessages()
    {
        SingletonManager.Instance.Clear();
    }
}
