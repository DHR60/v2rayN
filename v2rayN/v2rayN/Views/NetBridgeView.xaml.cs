namespace v2rayN.Views;

public partial class NetBridgeView
{
    public NetBridgeView()
    {
        InitializeComponent();

        ViewModel = new NetBridgeViewModel();

        this.WhenActivated(disposables =>
        {
            this.BindCommand(ViewModel, vm => vm.SaveRulesCmd, v => v.btnSave).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.EnableNetBridge, v => v.togEnableNetBridge.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnabletDnsViaProxy, v => v.togEnabletDnsViaProxy.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.RuleProcess, v => v.txtRuleProcess.Text).DisposeWith(disposables);

            ViewModel.Interaction.RegisterHandler(async interaction =>
            {
                var (action, obj) = interaction.Input;
                var result = await UpdateViewHandler(action, obj);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);
        });
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        return await Task.FromResult(true);
    }
}
