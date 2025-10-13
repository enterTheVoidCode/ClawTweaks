using Shared.Data;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;

namespace XboxGamingBar.Data
{
    internal class WidgetProperties : FunctionalProperties
    {
        public WidgetProperties(params FunctionalProperty[] inProperties) : base(inProperties) { }

        protected override Task<AppServiceResponseStatus> SendResponse(AppServiceRequest request, ValueSet response)
        {
            return request.SendResponseAsync(response).AsTask();
        }

        public async Task Sync()
        {
            foreach (var property in properties)
            {
                await property.Value.Sync();
            }
        }
    }
}
