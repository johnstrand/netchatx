using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stanza.Protocol.Xeps.Omemo;
using Stanza.Storage.Models;
using Stanza.Storage.Repositories;

namespace Stanza.Gui.ViewModels;

public sealed partial class OmemoDeviceItemViewModel : ViewModelBase
{
    private readonly OmemoRepository _omemoRepo;
    private readonly string _accountJid;
    private readonly string _remoteJid;

    [ObservableProperty]
    private uint _deviceId;

    [ObservableProperty]
    private string _fingerprint = string.Empty;

    [ObservableProperty]
    private OmemoTrustState _trustState;

    public bool IsTrusted => TrustState == OmemoTrustState.Trusted;

    public OmemoDeviceItemViewModel(
        OmemoRepository omemoRepo,
        string accountJid,
        string remoteJid,
        uint deviceId,
        string fingerprint,
        OmemoTrustState trustState)
    {
        _omemoRepo = omemoRepo;
        _accountJid = accountJid;
        _remoteJid = remoteJid;
        DeviceId = deviceId;
        Fingerprint = fingerprint;
        TrustState = trustState;
    }

    [RelayCommand]
    public async Task ToggleTrustAsync()
    {
        TrustState = TrustState == OmemoTrustState.Trusted ? OmemoTrustState.Untrusted : OmemoTrustState.Trusted;
        OnPropertyChanged(nameof(IsTrusted));
        await _omemoRepo.UpdateTrustStateAsync(_accountJid, _remoteJid, DeviceId, TrustState);
    }
}

public sealed partial class OmemoDetailsViewModel : ViewModelBase
{
    [ObservableProperty]
    private uint _localDeviceId;

    [ObservableProperty]
    private string _localFingerprint = string.Empty;

    [ObservableProperty]
    private string _contactJid = string.Empty;

    public ObservableCollection<OmemoDeviceItemViewModel> Devices { get; } = [];

    public void LoadDevices(IEnumerable<OmemoDeviceItemViewModel> devices)
    {
        Devices.Clear();
        foreach (var d in devices)
        {
            Devices.Add(d);
        }
    }
}
