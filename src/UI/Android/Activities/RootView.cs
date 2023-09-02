﻿using Android.Content;
using Android.Hardware.Usb;
using Android.Views;
using Hoho.Android.UsbSerial.driver;
using Hoho.Android.UsbSerial.Extensions;
using MvvmCross;
using MvvmCross.Base;
using MvvmCross.Binding.BindingContext;
using MvvmCross.Platforms.Android.Presenters.Attributes;
using MvvmCross.Platforms.Android.Views;
using MvvmCross.ViewModels;
using OSDPBench.Core.Interactions;
using OSDPBench.Core.Platforms;
using OSDPBench.Core.ViewModels;
using OSDPBench.UI.Android.Platform;
using static Android.App.AlertDialog;

namespace OSDPBench.UI.Android.Activities;

[MvxActivityPresentation]
[Activity(Theme = "@style/Theme.AppCompat.Light",
    WindowSoftInputMode = SoftInput.AdjustPan)]
public class RootView : MvxActivity<RootViewModel>
{
    private UsbManager? _usbManager; 
    
    protected override async void OnCreate(Bundle bundle)
    {
        base.OnCreate(bundle);

        await InitializeUsbService();

        SetContentView(Resource.Layout.RootView);
    }
    
    // ReSharper disable once RedundantOverriddenMember
    protected override void OnPause()
    {
        base.OnPause();

        // Need a top level activity
        //var connection = Mvx.IoCProvider.Resolve<ISerialPortConnection>() as AndroidSerialPortConnection;
        //connection?. Close();
    }

    // ReSharper disable once RedundantOverriddenMember
    protected override void OnResume()
    {
        base.OnResume();

        // Need a top level activity
        //var connection = Mvx.IoCProvider.Resolve<ISerialPortConnection>() as AndroidSerialPortConnection;
        //connection?.Open();
    }
    
    protected override void OnViewModelSet()
    {
        base.OnViewModelSet();

        BindingContext.DataContext = ViewModel;

        using var set = this.CreateBindingSet<RootView, RootViewModel>();

        set.Bind(this).For(view => view.AlertInteraction).To(viewModel => viewModel.AlertInteraction).OneWay();
        set.Bind(this).For(view => view.YesNoInteraction).To(viewModel => viewModel.YesNoInteraction).OneWay();
        set.Apply();
    }
    
    private IMvxInteraction<Alert> _alertInteraction = null!;
    
    // ReSharper disable once MemberCanBePrivate.Global
    public IMvxInteraction<Alert> AlertInteraction
    {
        get => _alertInteraction;
        set
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_alertInteraction != null) _alertInteraction.Requested -= OnAlertInteractionRequested!;

            _alertInteraction = value; 
            _alertInteraction.Requested += OnAlertInteractionRequested!;
        }
    }

    private void OnAlertInteractionRequested(object sender, MvxValueEventArgs<Alert> eventArgs)
    {
        var dialog = new Builder(this);
        dialog.SetTitle("OSDP Bench")?.SetMessage(eventArgs.Value.Message)
            ?.SetPositiveButton("OK", (_, _) => { });
        var alert = dialog.Create();
        alert?.Show();
    }

    private IMvxInteraction<YesNoQuestion> _yesNoInteraction = null!;
    public IMvxInteraction<YesNoQuestion> YesNoInteraction
    {
        get => _yesNoInteraction;
        set
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_yesNoInteraction != null) _yesNoInteraction.Requested -= OnYesNoInteractionRequested!;

            _yesNoInteraction = value;
            _yesNoInteraction.Requested += OnYesNoInteractionRequested!;
        }
    }

    private void OnYesNoInteractionRequested(object sender, MvxValueEventArgs<YesNoQuestion> eventArgs)
    {
        var dialog = new Builder(this);
        dialog.SetTitle("OSDP Bench")?.SetMessage(eventArgs.Value.Question)
            ?.SetPositiveButton("Yes", (_, _) => { eventArgs.Value.YesNoCallback(true); })
            ?.SetNegativeButton("No", (_, _) => { eventArgs.Value.YesNoCallback(false); });
        var alert = dialog.Create();
        alert?.Show();
    }

    private async Task InitializeUsbService()
    {
        _usbManager = GetSystemService(Context.UsbService) as UsbManager;
        var drivers = await FindAllDriversAsync(_usbManager);
        
        var port = drivers.FirstOrDefault()?.Ports.FirstOrDefault();

        if (_usbManager != null && port != null)
        {
            var permissionGranted = await _usbManager.RequestPermissionAsync(port.Driver.Device, this);
            if (permissionGranted)
            {
                var connection = Mvx.IoCProvider.GetSingleton<ISerialPortConnection>() as AndroidSerialPortConnection;
                connection?.GetSerialPorts(drivers);
                connection?.LoadPort(_usbManager, port);
                ViewModel.ScanSerialPortsCommand.Execute(null);
            }
            else
            {
                var dialog = new Builder(this);
                dialog.SetTitle("OSDP Bench")?.SetMessage("Unable to discover without permission to use USB port.")
                    ?.SetPositiveButton("OK", (_, _) => { });
                var alert = dialog.Create();
                alert?.Show();

            }
        }
    }

    public static async Task<IList<IUsbSerialDriver>> FindAllDriversAsync(UsbManager? usbManager)
    {
        // adding a custom driver to the default probe table
        var table = UsbSerialProber.DefaultProbeTable;
        table.AddProduct(0x1b4f, 0x0008, typeof(CdcAcmSerialDriver)); // IOIO OTG

        table.AddProduct(0x09D8, 0x0420, typeof(CdcAcmSerialDriver)); // Elatec TWN4

        var prober = new UsbSerialProber(table);
        return await prober.FindAllDriversAsync(usbManager);
    }
}