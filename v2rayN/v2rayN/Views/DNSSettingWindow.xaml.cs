using System.Reactive.Disposables;
using System.Windows;
using ReactiveUI;

namespace v2rayN.Views;

public partial class DNSSettingWindow
{
    private static Config _config;

    public DNSSettingWindow()
    {
        InitializeComponent();

        this.Owner = Application.Current.MainWindow;
        _config = AppHandler.Instance.Config;

        ViewModel = new DNSSettingViewModel(UpdateViewHandler);

        Global.DomainStrategy4Freedoms.ForEach(it =>
        {
            cmbRayFreedomDNSStrategy.Items.Add(it);
        });
        Global.SingboxDomainStrategy4Out.ForEach(it =>
        {
            cmbSBDirectDNSStrategy.Items.Add(it);
            cmbSBRemoteDNSStrategy.Items.Add(it);
        });
        Global.DomainDirectDNSAddress.ForEach(it =>
        {
            cmbDirectDNS.Items.Add(it);
            cmbSBResolverDNS.Items.Add(it);
        });
        cmbSBResolverDNS.Items.Add("dhcp://auto,localhost");
        Global.DomainRemoteDNSAddress.ForEach(it =>
        {
            cmbRemoteDNS.Items.Add(it);
        });
        Global.DomainPureIPDNSAddress.ForEach(it =>
        {
            cmbSBFinalResolverDNS.Items.Add(it);
        });
        cmbSBFinalResolverDNS.Items.Add("dhcp://auto,localhost");

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.UseSystemHosts, v => v.togUseSystemHosts.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AddCommonHosts, v => v.togAddCommonHosts.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.FakeIP, v => v.togFakeIP.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.BlockBindingQuery, v => v.togBlockBindingQuery.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DirectDNS, v => v.cmbDirectDNS.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.RemoteDNS, v => v.cmbRemoteDNS.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SingboxOutboundsResolveDNS, v => v.cmbSBResolverDNS.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SingboxFinalResolveDNS, v => v.cmbSBFinalResolverDNS.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.RayStrategy4Freedom, v => v.cmbRayFreedomDNSStrategy.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SingboxStrategy4Direct, v => v.cmbSBDirectDNSStrategy.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SingboxStrategy4Proxy, v => v.cmbSBRemoteDNSStrategy.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Hosts, v => v.txtHosts.Text).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.SaveCmd, v => v.btnSave).DisposeWith(disposables);
        });
        WindowsUtils.SetDarkBorder(this, AppHandler.Instance.Config.UiItem.CurrentTheme);
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.CloseWindow:
                this.DialogResult = true;
                break;
        }
        return await Task.FromResult(true);
    }

    private void linkDnsObjectDoc_Click(object sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart("https://xtls.github.io/config/dns.html#dnsobject");
    }

    private void linkDnsSingboxObjectDoc_Click(object sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart("https://sing-box.sagernet.org/zh/configuration/dns/");
    }
}
