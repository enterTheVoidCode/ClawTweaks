using Shared.Enums;
using System;
using System.Linq;
using System.Threading;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for the fan curve graph. Manages all 10 fan speed values as a comma-separated string.
    /// Values are fan speed percentages (0-100) for temperature thresholds 10°C to 100°C.
    /// Includes debouncing to avoid sending too many updates during drag operations.
    /// </summary>
    internal class LegionFanCurveGraphProperty : WidgetProperty<string>
    {
        private readonly Page _owner;
        private Action<int[]> _graphUpdateCallback;
        private Timer _debounceTimer;
        private string _pendingValue;
        private readonly object _debounceLock = new object();
        private const int DEBOUNCE_MS = 500;

        // Default fan curve values (Legion Go defaults)
        public static readonly int[] DefaultCurve = { 44, 48, 55, 60, 71, 79, 87, 87, 100, 100 };

        // Minimum fan speeds (%) for each temperature threshold (46°C to 75°C)
        // ~30% below Legion Space constraints to allow lower fan speeds
        private static readonly int[] MinSpeeds = { 30, 33, 33, 36, 40, 46, 58, 62, 66, 70 };

        public LegionFanCurveGraphProperty(Page owner)
            : base(FormatCurveData(DefaultCurve), null, Function.LegionFanCurveData)
        {
            _owner = owner;
        }

        /// <summary>
        /// Sets a callback to update the graph UI when values change from helper
        /// </summary>
        public void SetGraphUpdateCallback(Action<int[]> callback)
        {
            _graphUpdateCallback = callback;
        }

        /// <summary>
        /// Converts the string value to an array of integers
        /// </summary>
        public int[] GetCurveValues()
        {
            return ParseCurveData(Value);
        }

        /// <summary>
        /// Sets curve values from the graph UI with debouncing
        /// </summary>
        public void SetCurveValuesDebounced(int[] values)
        {
            if (values == null || values.Length != 10)
            {
                Logger.Warn("Invalid curve values array");
                return;
            }

            var newValue = FormatCurveData(values);

            lock (_debounceLock)
            {
                _pendingValue = newValue;

                if (_debounceTimer == null)
                {
                    _debounceTimer = new Timer(DebounceCallback, null, DEBOUNCE_MS, Timeout.Infinite);
                }
                else
                {
                    _debounceTimer.Change(DEBOUNCE_MS, Timeout.Infinite);
                }
            }
        }

        /// <summary>
        /// Sets curve values immediately without debouncing (for initialization)
        /// </summary>
        public void SetCurveValuesImmediate(int[] values)
        {
            if (values == null || values.Length != 10)
            {
                Logger.Warn("Invalid curve values array");
                return;
            }

            var newValue = FormatCurveData(values);
            SetValue(newValue);
        }

        private void DebounceCallback(object state)
        {
            string valueToSend;
            lock (_debounceLock)
            {
                valueToSend = _pendingValue;
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }

            if (!string.IsNullOrEmpty(valueToSend))
            {
                Logger.Info($"Sending debounced fan curve: {valueToSend}");
                SetValue(valueToSend);
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            // Parse and invoke callback for graph UI update
            if (_graphUpdateCallback != null && _owner != null)
            {
                var values = GetCurveValues();
                await _owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    _graphUpdateCallback(values);
                });
            }
        }

        /// <summary>
        /// Formats an array of fan speeds into a comma-separated string
        /// </summary>
        public static string FormatCurveData(int[] values)
        {
            if (values == null || values.Length != 10)
                return FormatCurveData(DefaultCurve);
            return string.Join(",", values);
        }

        /// <summary>
        /// Parses a comma-separated string into an array of fan speeds.
        /// Enforces minimum fan speed constraints for each temperature threshold.
        /// </summary>
        public static int[] ParseCurveData(string data)
        {
            if (string.IsNullOrEmpty(data))
                return (int[])DefaultCurve.Clone();

            try
            {
                var parts = data.Split(',');
                if (parts.Length != 10)
                    return (int[])DefaultCurve.Clone();

                var values = new int[10];
                for (int i = 0; i < 10; i++)
                {
                    int parsed = int.Parse(parts[i].Trim());
                    // Enforce minimum and maximum for each point
                    values[i] = Math.Max(MinSpeeds[i], Math.Min(100, parsed));
                }
                return values;
            }
            catch
            {
                return (int[])DefaultCurve.Clone();
            }
        }
    }

    /// <summary>
    /// Read-only property for current CPU temperature from the helper.
    /// Used to display temperature indicator on the fan curve graph.
    /// </summary>
    internal class LegionCPUTempProperty : WidgetProperty<int>
    {
        private readonly Page _owner;
        private Action<int> _tempUpdateCallback;

        public LegionCPUTempProperty(Page owner)
            : base(0, null, Function.LegionCPUCurrentTemp)
        {
            _owner = owner;
        }

        /// <summary>
        /// Sets a callback to update the temperature indicator on the graph
        /// </summary>
        public void SetTempUpdateCallback(Action<int> callback)
        {
            _tempUpdateCallback = callback;
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            Logger.Info($"LegionCPUTempProperty.SetValue called with value: {newValue}");
            return base.SetValue(newValue, updatedTime);
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Logger.Info($"LegionCPUTempProperty.NotifyPropertyChanged: Value={Value}, callback={_tempUpdateCallback != null}");

            if (_tempUpdateCallback != null && _owner != null)
            {
                var temp = Value;
                await _owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    Logger.Info($"Invoking temp callback with {temp}°C");
                    _tempUpdateCallback(temp);
                });
            }
        }
    }

    /// <summary>
    /// Read-only property for current CPU fan RPM from the helper.
    /// Used to display fan speed in the fan curve card header.
    /// </summary>
    internal class LegionCPUFanRPMProperty : WidgetProperty<int>
    {
        private readonly Page _owner;
        private Action<int> _rpmUpdateCallback;

        public LegionCPUFanRPMProperty(Page owner)
            : base(0, null, Function.LegionCPUFanRPM)
        {
            _owner = owner;
        }

        /// <summary>
        /// Sets a callback to update the fan RPM display
        /// </summary>
        public void SetRPMUpdateCallback(Action<int> callback)
        {
            _rpmUpdateCallback = callback;
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (_rpmUpdateCallback != null && _owner != null)
            {
                var rpm = Value;
                await _owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    _rpmUpdateCallback(rpm);
                });
            }
        }
    }

    /// <summary>
    /// Property to tell the helper when the fan curve graph is visible.
    /// The helper only pushes CPU temp and fan RPM updates when this is true.
    /// </summary>
    internal class LegionFanCurveVisibleProperty : WidgetProperty<bool>
    {
        public LegionFanCurveVisibleProperty()
            : base(false, null, Function.LegionFanCurveVisible)
        {
        }

        public void SetVisible(bool visible)
        {
            SetValue(visible);
        }
    }
}
