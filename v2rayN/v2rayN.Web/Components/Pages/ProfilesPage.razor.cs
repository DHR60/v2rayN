using Microsoft.AspNetCore.Components.Web;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Events;
using ServiceLib.Helper;
using ServiceLib.Manager;
using ServiceLib.Models;
using ServiceLib.ViewModels;

namespace v2rayN.Web.Components.Pages;

public partial class ProfilesPage
{
    private static Config _config;
    private HashSet<ProfileItemModel> selectedItems = new();

    public ProfilesPage()
    {
        _config = AppManager.Instance.Config;

        ViewModel = new ProfilesViewModel(UpdateViewHandler);
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        return await Task.FromResult(true);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        _ = RefreshProfiles();
    }

    private async void AddProfileTest()
    {
        ProfileItem item = new()
        {
            IndexId = Utils.GetGuid(false),
            Address = "example.com",
            Port = 443,
            Id = Utils.GetGuid(false),
            Network = "ws",
            Security = "tls",
            Remarks = "New Profile",
            ConfigType = EConfigType.VMess,
            CoreType = ECoreType.Xray,
        };
        await SQLiteHelper.Instance.ReplaceAsync(item);
        await RefreshProfiles();
    }

    private async Task RefreshProfiles()
    {
        AppEvents.ProfilesRefreshRequested.Publish();

        await Task.Delay(200);
    }

    private void SetSelectedItem(ProfileItemModel model)
    {
        selectedItems = new HashSet<ProfileItemModel> { model };
        ViewModel?.SelectedProfiles = selectedItems.ToList();
        ViewModel?.SelectedProfile = model;
    }

    private async Task RemoveProfile(ProfileItemModel model)
    {
        SetSelectedItem(model);

        await RemoveProfiles();
    }

    private async Task RemoveProfiles()
    {
        if (ViewModel == null || ViewModel.SelectedProfiles.Count == 0)
        {
            return;
        }
        await ViewModel!.RemoveServerAsync();
    }

    private async Task SetDefaultServer(ProfileItemModel model)
    {
        SetSelectedItem(model);
        if (ViewModel != null)
        {
            await ViewModel.SetDefaultServer();
        }
    }

    private void OnServerFilterKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or "Return")
        {
            ViewModel?.RefreshServers();
        }
    }
}
