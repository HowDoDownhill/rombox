using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

// Windows: virtual Xbox 360 controllers through the ViGEmBus driver. Windows offers no way to add
// a controller that games can see without a driver, so the host needs ViGEmBus installed once.
// This file must only be reached on Windows; see StreamVirtualPadBackends.CreateViGEm.
public sealed class ViGEmPadBackend : IStreamVirtualPadBackend
{
    private readonly Action<string> log;
    private ViGEmClient client;

    public string UnavailableReason { get; }

    public ViGEmPadBackend(Action<string> log)
    {
        this.log = log;

        try
        {
            client = new ViGEmClient();
        }
        catch (Exception failure)
        {
            UnavailableReason = "the virtual controller driver (ViGEmBus) is not installed on this PC.";
            log?.Invoke($"[Stream] could not reach ViGEmBus: {failure.GetType().Name}: {failure.Message}");
        }
    }

    public IStreamVirtualPad CreatePad()
    {
        try
        {
            IXbox360Controller controller = client.CreateXbox360Controller();
            controller.AutoSubmitReport = false;
            controller.Connect();
            return new Pad(controller, log);
        }
        catch (Exception failure)
        {
            log?.Invoke($"[Stream] could not plug in a virtual controller: {failure.GetType().Name}: {failure.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        client?.Dispose();
        client = null;
    }

    private sealed class Pad : IStreamVirtualPad
    {
        private readonly IXbox360Controller controller;
        private readonly Action<string> log;
        private bool reportedFailure;

        public Pad(IXbox360Controller controller, Action<string> log)
        {
            this.controller = controller;
            this.log = log;
        }

        public void Submit(in StreamPadState state)
        {
            try
            {
                controller.SetButtonsFull((ushort)state.Buttons);
                controller.SetSliderValue(Xbox360Slider.LeftTrigger, state.LeftTrigger);
                controller.SetSliderValue(Xbox360Slider.RightTrigger, state.RightTrigger);
                controller.SetAxisValue(Xbox360Axis.LeftThumbX, state.LeftX);
                controller.SetAxisValue(Xbox360Axis.LeftThumbY, state.LeftY);
                controller.SetAxisValue(Xbox360Axis.RightThumbX, state.RightX);
                controller.SetAxisValue(Xbox360Axis.RightThumbY, state.RightY);
                controller.SubmitReport();
            }
            catch (Exception failure)
            {
                if (!reportedFailure)
                {
                    reportedFailure = true;
                    log?.Invoke($"[Stream] a virtual controller rejected input: {failure.GetType().Name}: {failure.Message}");
                }
            }
        }

        public void Dispose()
        {
            try
            {
                controller.Disconnect();
            }
            catch (Exception)
            {
            }
        }
    }
}
