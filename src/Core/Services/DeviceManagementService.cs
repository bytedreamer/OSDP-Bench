﻿using System.Collections;
using System.Text;
using OSDP.Net;
using OSDP.Net.Connections;
using OSDP.Net.Model.ReplyData;
using OSDP.Net.PanelCommands.DeviceDiscover;
using OSDPBench.Core.Models;
using CommunicationConfiguration = OSDP.Net.Model.CommandData.CommunicationConfiguration;
using ManufacturerSpecific = OSDP.Net.Model.CommandData.ManufacturerSpecific;

namespace OSDPBench.Core.Services;

/// <summary>
/// Class DeviceManagementService.
/// Implements the <see cref="IDeviceManagementService" />
/// </summary>
/// <seealso cref="IDeviceManagementService" />
public class DeviceManagementService : IDeviceManagementService
{
    private readonly ControlPanel _panel = new ();

    private Guid _connectionId;
    private bool _isDiscovering;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceManagementService"/> class.
    /// </summary>
    /// <exception cref="ArgumentNullException">panel</exception>
    public DeviceManagementService()
    {
        _panel.ConnectionStatusChanged += (_, args) =>
        {
            if (_isDiscovering) return;

            IsConnected = args.IsConnected;
            if (IsConnected)
            {
                Task.Run(async () =>
                {
                    IdentityLookup = new IdentityLookup(await _panel.IdReport(_connectionId, Address));
                    CapabilitiesLookup = new CapabilitiesLookup(await _panel.DeviceCapabilities(_connectionId, Address));

                    OnDeviceLookupsChanged();
                });
            }
            else
            {
                IdentityLookup = null;
                CapabilitiesLookup = null;

                OnDeviceLookupsChanged();
            }
            
            OnConnectionStatusChange(args.IsConnected);
        };

        _panel.NakReplyReceived += (_, args) =>
        {
            OnNakReplyReceived(ToFormattedText(args.Nak.ErrorCode));
        };

        _panel.RawCardDataReplyReceived += (_, args) => OnCardReadReceived(FormatData(args.RawCardData.Data));
    }
        
    /// <inheritdoc />
    public IdentityLookup? IdentityLookup { get; private set; }

    /// <inheritdoc />
    public CapabilitiesLookup? CapabilitiesLookup { get; private set; }

    /// <inheritdoc />
    public byte Address { get; private set; }

    /// <inheritdoc />
    public uint BaudRate { get; private set; }

    /// <inheritdoc />
    public bool UsesDefaultSecurityKey { get; private set; }
    
    public bool IsConnected { get; private set; }

    public async Task Connect(IOsdpConnection connection, byte address)
    {
        await Shutdown();

        Address = address;
        BaudRate = (uint)connection.BaudRate;

        _connectionId = _panel.StartConnection(connection);
        _panel.AddDevice(_connectionId, address, true, false);
    }

    public async Task<DiscoveryResult> DiscoverDevice(IEnumerable<IOsdpConnection> connections, DiscoveryProgress progress, CancellationToken cancellationToken)
    {
        await Shutdown();

        _isDiscovering = true;
        DiscoveryResult results;
        try
        {
            results = await DiscoveryRoutines(connections, progress, cancellationToken);
        }
        finally
        {
            _isDiscovering = false;
        }

        if (results.Status != DiscoveryStatus.Succeeded) return results;

        Address = results.Address;
        BaudRate = (uint)results.Connection.BaudRate;
        IdentityLookup = new IdentityLookup(results.Id);
        CapabilitiesLookup = new CapabilitiesLookup(results.Capabilities);
        UsesDefaultSecurityKey = results.UsesDefaultSecurityKey;

        OnDeviceLookupsChanged();

        _connectionId = _panel.StartConnection(results.Connection);
        _panel.AddDevice(_connectionId, Address, CapabilitiesLookup.CRC, false);

        return results;
    }

    private async Task<DiscoveryResult> DiscoveryRoutines (IEnumerable<IOsdpConnection> connections, DiscoveryProgress progress, CancellationToken cancellationToken)
    {
        IdentityLookup = null;
        CapabilitiesLookup = null;

        var options = new DiscoveryOptions
        {
            ProgressCallback = progress,
            ResponseTimeout = TimeSpan.FromSeconds(1),
            CancellationToken = cancellationToken
        };

        var results = await _panel.DiscoverDevice(connections, options);

        return results;
    }

    /// <inheritdoc /> 
    public async Task<CommunicationParameters> SetCommunicationCommand(
        CommunicationParameters communicationParameters)
    {
        try
        {
            var result = await _panel.CommunicationConfiguration(_connectionId, Address,
                new CommunicationConfiguration(communicationParameters.Address,
                    (int)communicationParameters.BaudRate));

            Address = result.Address;
            BaudRate = (uint)result.BaudRate;

            return new CommunicationParameters(communicationParameters.PortName, BaudRate, Address);
        }
        catch (TimeoutException)
        {
            return communicationParameters;
        }
    }

    /// <inheritdoc />
    public async Task ResetDevice(ISerialPortConnectionService connectionService)
    {
        await Shutdown();

        _connectionId = _panel.StartConnection(connectionService, TimeSpan.Zero);

        _panel.AddDevice(_connectionId, Address, false, false);

        const int maximumAttempts = 15;
        const int requiredNumberOfAcks = 10;
        int totalAcks = 0;
        int totalAttempts = 0;
        while (totalAttempts++ < maximumAttempts && totalAcks < requiredNumberOfAcks)
        {
            try
            {
                var result = await _panel.ManufacturerSpecificCommand(_connectionId, Address,
                    new ManufacturerSpecific(new byte[] { 0xCA, 0x44, 0x6C }, new byte[] { 0x05 }));

                if (result.Ack)
                {
                    totalAcks++;
                }
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        if (totalAcks < requiredNumberOfAcks)
        {
            throw new Exception("Reset commands were not accepted.");
        }
    }

    private static string ToFormattedText(ErrorCode value)
    {
        var builder = new StringBuilder();

        foreach (var character in value.ToString())
        {
            if (char.IsUpper(character))
            {
                builder.Append(" ");
            }

            builder.Append(character);
        }

        return builder.ToString().TrimStart();
    }

    /// <inheritdoc />
    public async Task Shutdown()
    {
        IdentityLookup = null;
        CapabilitiesLookup = null;
        
        await _panel.Shutdown();
    }

    /// <inheritdoc />
    public event EventHandler<bool>? ConnectionStatusChange;
    protected virtual void OnConnectionStatusChange(bool isConnected)
    {
        ConnectionStatusChange?.Invoke(this, isConnected);
    }

    public event EventHandler? DeviceLookupsChanged;
    protected virtual void OnDeviceLookupsChanged()
    {
        DeviceLookupsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler<string>? NakReplyReceived;

    protected virtual void OnNakReplyReceived(string errorMessage)
    {
        NakReplyReceived?.Invoke(this, errorMessage);
    }

    /// <inheritdoc />
    public event EventHandler<string>? CardReadReceived;
    protected virtual void OnCardReadReceived(string data)
    {
        CardReadReceived?.Invoke(this, data);
    }

    // ReSharper disable once UnusedMember.Local
    private static string FormatData(BitArray bitArray)
    {
        var builder = new StringBuilder();
        foreach (bool bit in bitArray)
        {
            builder.Append(bit ? "1" : "0");
        }

        return builder.ToString();
    }
}