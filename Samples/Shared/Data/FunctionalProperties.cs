using NLog;
using Shared.Enums;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;

namespace Shared.Data
{
    public abstract class FunctionalProperties
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        protected readonly Dictionary<Function, FunctionalProperty> properties;

        public FunctionalProperties(params FunctionalProperty[] inProperties)
        {
            properties = new Dictionary<Function, FunctionalProperty>();
            foreach (var property in inProperties)
            {
                if (!properties.ContainsKey(property.Function))
                {
                    properties.Add(property.Function, property);
                }
                else
                {
                    Logger.Warn($"Duplicated property {property.Function}");
                }
            }
        }

        /// <summary>
        /// Try to get a property by its function type.
        /// </summary>
        public bool TryGetProperty(Function function, out FunctionalProperty property)
        {
            lock (properties)
            {
                return properties.TryGetValue(function, out property);
            }
        }

        /// <summary>
        /// Add a property dynamically (thread-safe for background initialization).
        /// </summary>
        public void Add(FunctionalProperty property)
        {
            lock (properties)
            {
                if (!properties.ContainsKey(property.Function))
                {
                    properties.Add(property.Function, property);
                    Logger.Debug($"Added property {property.Function} dynamically");
                }
                else
                {
                    Logger.Warn($"Property {property.Function} already exists, skipping");
                }
            }
        }

        public async Task OnRequestReceived(AppServiceRequest request)
        {
            var function = (Function)request.Message[nameof(Function)];
            if (function == Function.None)
            {
                return;
            }

            FunctionalProperty property;
            lock (properties)
            {
                if (!properties.TryGetValue(function, out property))
                {
                    Logger.Error($"Property {function} not found.");
                    return;
                }
            }

            var command = (Command)request.Message[nameof(Command)];
            var response = new ValueSet();
            switch (command)
            {
                case Command.Get:
                    response = property.AddValueSetContent(response);
                    break;
                case Command.Set:
                    property.SetValue(request.Message[nameof(Content)], (long)request.Message[nameof(UpdatedTime)]);
                    response.Add(nameof(Content), "Success");
                    break;
                default:
                    Logger.Error($"Can't process command {command}");
                    break;
            }
            var sendResponseResult = await SendResponse(request, response);
            Logger.Debug($"Sent response {function} {sendResponseResult}.");
        }

        protected abstract Task<AppServiceResponseStatus> SendResponse(AppServiceRequest request, ValueSet response);
    }
}
